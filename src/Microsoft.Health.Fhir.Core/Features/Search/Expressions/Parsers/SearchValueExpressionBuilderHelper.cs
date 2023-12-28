// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    internal class SearchValueExpressionBuilderHelper : ISearchValueVisitor
    {
        private const decimal ApproximateMultiplier = .1M;

        private string _searchParameterName;
        private SearchModifier _modifier;
        private SearchComparator _comparator;
        private int? _componentIndex;

        private Expression _outputExpression;

        public Expression Build(
            string searchParameterName,
            SearchModifier modifier,
            SearchComparator comparator,
            int? componentIndex,
            ISearchValue searchValue)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterName, nameof(searchParameterName));
            Debug.Assert(
                Enum.IsDefined(typeof(SearchComparator), comparator),
                "Invalid comparator.");
            EnsureArg.IsNotNull(searchValue, nameof(searchValue));

            _searchParameterName = searchParameterName;
            _modifier = modifier;
            _comparator = comparator;
            _componentIndex = componentIndex;

            searchValue.AcceptVisitor(this);

            return _outputExpression;
        }

        void ISearchValueVisitor.Visit(CompositeSearchValue composite)
        {
            // Composite search values will be broken down into individual components,
            // and therefore this method should not be called.
            throw new InvalidOperationException("The composite search value should have been broken down into components and handled individually.");
        }

        void ISearchValueVisitor.Visit(DateTimeSearchValue dateTime)
        {
            EnsureArg.IsNotNull(dateTime, nameof(dateTime));

            if (_modifier != null)
            {
                ThrowModifierNotSupported();
            }

            // Based on spec here: http://hl7.org/fhir/search.html#prefix
            switch (_comparator)
            {
                case SearchComparator.Eq:
                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, _componentIndex, dateTime.Start),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, _componentIndex, dateTime.End));
                    break;
                case SearchComparator.Ne:
                    _outputExpression = Expression.Or(
                        Expression.LessThan(FieldName.DateTimeStart, _componentIndex, dateTime.Start),
                        Expression.GreaterThan(FieldName.DateTimeEnd, _componentIndex, dateTime.End));
                    break;
                case SearchComparator.Lt:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeStart, _componentIndex, dateTime.Start);
                    break;
                case SearchComparator.Gt:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeEnd, _componentIndex, dateTime.End);
                    break;
                case SearchComparator.Le:
                    _outputExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, _componentIndex, dateTime.End);
                    break;
                case SearchComparator.Ge:
                    _outputExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, _componentIndex, dateTime.Start);
                    break;
                case SearchComparator.Sa:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeStart, _componentIndex, dateTime.End);
                    break;
                case SearchComparator.Eb:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeEnd, _componentIndex, dateTime.Start);
                    break;
                case SearchComparator.Ap:
                    var startTicks = dateTime.Start.UtcTicks;
                    var endTicks = dateTime.End.UtcTicks;

                    var differenceTicks = (long)((Clock.UtcNow.Ticks - Math.Max(startTicks, endTicks)) * ApproximateMultiplier);

                    var approximateStart = dateTime.Start.AddTicks(-differenceTicks);
                    var approximateEnd = dateTime.End.AddTicks(differenceTicks);

                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, _componentIndex, approximateStart),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, _componentIndex, approximateEnd));
                    break;
                default:
                    ThrowComparatorNotSupported();
                    break;
            }
        }

        void ISearchValueVisitor.Visit(NumberSearchValue number)
        {
            EnsureArg.IsNotNull(number, nameof(number));

            if (_modifier != null)
            {
                ThrowModifierNotSupported();
            }

            Debug.Assert(number.Low.HasValue && number.Low == number.High, "number low and high should be the same and not null");
            _outputExpression = GenerateNumberExpression(FieldName.Number, number.Low.Value);
        }

        void ISearchValueVisitor.Visit(QuantitySearchValue quantity)
        {
            EnsureArg.IsNotNull(quantity, nameof(quantity));

            if (_modifier != null)
            {
                ThrowModifierNotSupported();
            }

            var expressions = new List<Expression>(3);

            // Based on spec http://hl7.org/fhir/STU3/search.html#quantity,
            // The system is handled differently in quantity than token.
            if (!string.IsNullOrWhiteSpace(quantity.System))
            {
                expressions.Add(
                    Expression.StringEquals(FieldName.QuantitySystem, _componentIndex, quantity.System, false));
            }

            if (!string.IsNullOrWhiteSpace(quantity.Code))
            {
                expressions.Add(
                    Expression.StringEquals(FieldName.QuantityCode, _componentIndex, quantity.Code, false));
            }

            Debug.Assert(quantity.Low.HasValue && quantity.Low == quantity.High, "quantity low and high should be the same and not null");
            expressions.Add(GenerateNumberExpression(FieldName.Quantity, quantity.Low.Value));

            if (expressions.Count == 1)
            {
                _outputExpression = expressions[0];
            }
            else
            {
                _outputExpression = Expression.And(expressions.ToArray());
            }
        }

        void ISearchValueVisitor.Visit(ReferenceSearchValue reference)
        {
            EnsureArg.IsNotNull(reference, nameof(reference));

            if (_modifier != null && _modifier.SearchModifierCode != SearchModifierCode.Type)
            {
                ThrowModifierNotSupported();
            }

            EnsureOnlyEqualComparatorIsSupported();

            if (reference.BaseUri != null)
            {
                // The reference is external.
                _outputExpression = Expression.And(
                    Expression.StringEquals(FieldName.ReferenceBaseUri, _componentIndex, reference.BaseUri.ToString(), false),
                    Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
            }
            else if (reference.ResourceType == null)
            {
                // Only resource id is specified.
                _outputExpression = Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false);
            }
            else if (reference.Kind == ReferenceKind.Internal)
            {
                // The reference must be internal.
                _outputExpression = Expression.And(
                    Expression.Missing(FieldName.ReferenceBaseUri, _componentIndex),
                    Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
            }
            else
            {
                // The reference can be internal or external.
                _outputExpression = Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
            }
        }

        void ISearchValueVisitor.Visit(StringSearchValue s)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            EnsureOnlyEqualComparatorIsSupported();

            if (_modifier == null)
            {
                // Based on spec http://hl7.org/fhir/STU3/search.html#string,
                // is case-insensitive search so we will normalize into lower case for search.
                _outputExpression = Expression.StartsWith(FieldName.String, _componentIndex, s.String, true);
            }
            else if (_modifier.SearchModifierCode == SearchModifierCode.Exact)
            {
                _outputExpression = Expression.StringEquals(FieldName.String, _componentIndex, s.String, false);
            }
            else if (_modifier.SearchModifierCode == SearchModifierCode.Contains)
            {
                // Based on spec http://hl7.org/fhir/STU3/search.html#modifiers,
                // contains is case-insensitive search so we will normalize into lower case for search.
                _outputExpression = Expression.Contains(FieldName.String, _componentIndex, s.String, true);
            }
            else
            {
                ThrowModifierNotSupported();
            }
        }

        void ISearchValueVisitor.Visit(TokenSearchValue token)
        {
            EnsureArg.IsNotNull(token, nameof(token));

            EnsureOnlyEqualComparatorIsSupported();

            if (_modifier == null)
            {
                _outputExpression = BuildEqualityExpression();
            }
            else if (_modifier.SearchModifierCode == SearchModifierCode.Not)
            {
                _outputExpression = Expression.Not(BuildEqualityExpression());
            }
            else if (_modifier.SearchModifierCode == SearchModifierCode.Above ||
                     _modifier.SearchModifierCode == SearchModifierCode.Below ||
                     _modifier.SearchModifierCode == SearchModifierCode.In ||
                     _modifier.SearchModifierCode == SearchModifierCode.NotIn)
            {
                // These modifiers are not supported yet but will be supported eventually.
                ThrowModifierNotSupported();
            }
            else
            {
                ThrowModifierNotSupported();
            }

            Expression BuildEqualityExpression()
            {
                // Based on spec http://hl7.org/fhir/search.html#token,
                // we need to make sure to test if system is missing or not based on how it is supplied.
                if (token.System == null)
                {
                    // If the system is not supplied, then the token code is matched irrespective of the value of system.
                    return Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false);
                }
                else if (token.System.Length == 0)
                {
                    // If the system is empty, then the token is matched if there is no system property.
                    return Expression.And(
                        Expression.Missing(FieldName.TokenSystem, _componentIndex),
                        Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false));
                }
                else if (string.IsNullOrWhiteSpace(token.Code))
                {
                    // If the code is empty, then the token is matched if system is matched.
                    return Expression.StringEquals(FieldName.TokenSystem, _componentIndex, token.System, false);
                }
                else
                {
                    return Expression.And(
                        Expression.StringEquals(FieldName.TokenSystem, _componentIndex, token.System, false),
                        Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false));
                }
            }
        }

        void ISearchValueVisitor.Visit(UriSearchValue uri)
        {
            EnsureArg.IsNotNull(uri, nameof(uri));

            switch (_modifier?.SearchModifierCode)
            {
                case null:
                    _outputExpression = Expression.StringEquals(FieldName.Uri, _componentIndex, uri.Uri, false);
                    break;
                case SearchModifierCode.Above:
                    _outputExpression = Expression.And(
                        Expression.LeftSideStartsWith(FieldName.Uri, _componentIndex, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, _componentIndex, KnownUriSchemes.Urn, false));
                    break;
                case SearchModifierCode.Below:
                    _outputExpression = Expression.And(
                        Expression.StartsWith(FieldName.Uri, _componentIndex, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, _componentIndex, KnownUriSchemes.Urn, false));
                    break;
                default:
                    ThrowModifierNotSupported();
                    break;
            }
        }

        private void EnsureOnlyEqualComparatorIsSupported()
        {
            if (_comparator != SearchComparator.Eq)
            {
                throw new InvalidSearchOperationException(Core.Resources.OnlyEqualComparatorIsSupported);
            }
        }

        private void ThrowModifierNotSupported()
        {
            throw new InvalidSearchOperationException(
                string.Format(Core.Resources.ModifierNotSupported, _modifier, _searchParameterName));
        }

        private void ThrowComparatorNotSupported()
        {
            throw new InvalidSearchOperationException(
                string.Format(Core.Resources.ComparatorNotSupported, _comparator, _searchParameterName));
        }

        private Expression GenerateNumberExpression(FieldName fieldName, decimal number)
        {
            var modifierDecimal = number.GetPrescisionModifier();

            var lowerBound = number - modifierDecimal;
            var upperBound = number + modifierDecimal;

            switch (_comparator)
            {
                case SearchComparator.Ap:
                    var approximateModifier = Math.Abs(number * ApproximateMultiplier);
                    lowerBound -= approximateModifier;
                    upperBound += approximateModifier;
                    goto case SearchComparator.Eq;
                case SearchComparator.Eq:
                    return Expression.And(
                        Expression.GreaterThanOrEqual(fieldName, _componentIndex, lowerBound),
                        Expression.LessThanOrEqual(fieldName, _componentIndex, upperBound));
                case SearchComparator.Ne:
                    return Expression.Or(
                        Expression.LessThan(fieldName, _componentIndex, lowerBound),
                        Expression.GreaterThan(fieldName, _componentIndex, upperBound));
                case SearchComparator.Ge:
                    return Expression.GreaterThanOrEqual(fieldName, _componentIndex, number);
                case SearchComparator.Gt:
                case SearchComparator.Sa:
                    return Expression.GreaterThan(fieldName, _componentIndex, number);
                case SearchComparator.Le:
                    return Expression.LessThanOrEqual(fieldName, _componentIndex, number);
                case SearchComparator.Lt:
                case SearchComparator.Eb:
                    return Expression.LessThan(fieldName, _componentIndex, number);
                default:
                    ThrowComparatorNotSupported();
                    break;
            }

            return null;
        }
    }
}
