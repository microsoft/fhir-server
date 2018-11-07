// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    internal class SearchValueExpressionBuilderHelper : ISearchValueVisitor
    {
        private const double ApproximateDateTimeRangeMultiplier = .1;

        private string _searchParameterName;
        private SearchModifierCode? _modifier;
        private SearchComparator _comparator;

        private Expression _outputExpression;

        public Expression Build(
            string searchParameterName,
            SearchModifierCode? modifier,
            SearchComparator comparator,
            ISearchValue searchValue)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterName, nameof(searchParameterName));
            Debug.Assert(
                modifier == null || Enum.IsDefined(typeof(SearchModifierCode), modifier.Value),
                "Invalid modifier.");
            Debug.Assert(
                Enum.IsDefined(typeof(SearchComparator), comparator),
                "Invalid comparator.");
            EnsureArg.IsNotNull(searchValue, nameof(searchValue));

            _searchParameterName = searchParameterName;
            _modifier = modifier;
            _comparator = comparator;

            searchValue.AcceptVisitor(this);

            return _outputExpression;
        }

        void ISearchValueVisitor.Visit(CompositeSearchValue composite)
        {
            // Composite search values will be break down into individual component
            // and therefore this method should not be called.
            throw new InvalidOperationException("The composite search value should have been breaked down into components and have handled individually.");
        }

        void ISearchValueVisitor.Visit(DateTimeSearchValue dateTime)
        {
            EnsureArg.IsNotNull(dateTime, nameof(dateTime));

            if (_modifier != null)
            {
                ThrowModifierNotSupported();
            }

            // Based on spec here: http://hl7.org/fhir/STU3/search.html#prefix
            switch (_comparator)
            {
                case SearchComparator.Eq:
                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, dateTime.Start),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, dateTime.End));
                    break;
                case SearchComparator.Ne:
                    _outputExpression = Expression.Or(
                        Expression.LessThan(FieldName.DateTimeStart, dateTime.Start),
                        Expression.GreaterThan(FieldName.DateTimeEnd, dateTime.End));
                    break;
                case SearchComparator.Lt:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeStart, dateTime.Start);
                    break;
                case SearchComparator.Gt:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeEnd, dateTime.End);
                    break;
                case SearchComparator.Le:
                    _outputExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, dateTime.End);
                    break;
                case SearchComparator.Ge:
                    _outputExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, dateTime.Start);
                    break;
                case SearchComparator.Sa:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeStart, dateTime.End);
                    break;
                case SearchComparator.Eb:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeEnd, dateTime.Start);
                    break;
                case SearchComparator.Ap:
                    var startTicks = dateTime.Start.UtcTicks;
                    var endTicks = dateTime.End.UtcTicks;

                    var differenceTicks = (long)((Clock.UtcNow.Ticks - Math.Max(startTicks, endTicks)) * ApproximateDateTimeRangeMultiplier);

                    var approximateStart = dateTime.Start.AddTicks(-differenceTicks);
                    var approximateEnd = dateTime.End.AddTicks(differenceTicks);

                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, approximateStart),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, approximateEnd));
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

            _outputExpression = GenerateNumberExpression(FieldName.Number, number.Number);
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
                    Expression.StringEquals(FieldName.QuantitySystem, quantity.System, false));
            }

            if (!string.IsNullOrWhiteSpace(quantity.Code))
            {
                expressions.Add(
                    Expression.StringEquals(FieldName.QuantityCode, quantity.Code, false));
            }

            expressions.Add(GenerateNumberExpression(FieldName.Quantity, quantity.Quantity));

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

            if (_modifier != null)
            {
                ThrowModifierNotSupported();
            }

            EnsureOnlyEqualComparatorIsSupported();

            if (reference.BaseUri != null)
            {
                // The reference is external.
                _outputExpression = Expression.And(
                    Expression.StringEquals(FieldName.ReferenceBaseUri, reference.BaseUri.ToString(), false),
                    Expression.StringEquals(FieldName.ReferenceResourceType, reference.ResourceType.Value.ToString(), false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, reference.ResourceId, false));
            }
            else if (reference.ResourceType == null)
            {
                // Only resource id is specified.
                _outputExpression = Expression.StringEquals(FieldName.ReferenceResourceId, reference.ResourceId, false);
            }
            else if (reference.Kind == ReferenceKind.Internal)
            {
                // The reference must be internal.
                _outputExpression = Expression.And(
                    Expression.Missing(FieldName.ReferenceBaseUri),
                    Expression.StringEquals(FieldName.ReferenceResourceType, reference.ResourceType.Value.ToString(), false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, reference.ResourceId, false));
            }
            else
            {
                // The reference can be internal or external.
                _outputExpression = Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceType, reference.ResourceType.Value.ToString(), false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, reference.ResourceId, false));
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
                _outputExpression = Expression.StartsWith(FieldName.String, s.String, true);
            }
            else if (_modifier == SearchModifierCode.Exact)
            {
                _outputExpression = Expression.StringEquals(FieldName.String, s.String, false);
            }
            else if (_modifier == SearchModifierCode.Contains)
            {
                // Based on spec http://hl7.org/fhir/STU3/search.html#modifiers,
                // contains is case-insensitive search so we will normalize into lower case for search.
                _outputExpression = Expression.Contains(FieldName.String, s.String, true);
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
                // Based on spec http://hl7.org/fhir/STU3/search.html#token,
                // we need to make sure to test if system is missing or not based on how it is supplied.
                if (token.System == null)
                {
                    // If the system is not supplied, then the token code is matched irrespective of the value of system.
                    _outputExpression = Expression.StringEquals(FieldName.TokenCode, token.Code, false);
                }
                else if (token.System.Length == 0)
                {
                    // If the system is empty, then the token is matched if there is no system property.
                    _outputExpression = Expression.And(
                       Expression.Missing(FieldName.TokenSystem),
                       Expression.StringEquals(FieldName.TokenCode, token.Code, false));
                }
                else if (string.IsNullOrWhiteSpace(token.Code))
                {
                    // If the code is empty, then the token is matched if system is matched.
                    _outputExpression = Expression.StringEquals(FieldName.TokenSystem, token.System, false);
                }
                else
                {
                    _outputExpression = Expression.And(
                        Expression.StringEquals(FieldName.TokenSystem, token.System, false),
                        Expression.StringEquals(FieldName.TokenCode, token.Code, false));
                }
            }
            else if (_modifier == SearchModifierCode.Above ||
                _modifier == SearchModifierCode.Below ||
                _modifier == SearchModifierCode.In ||
                _modifier == SearchModifierCode.NotIn)
            {
                // These modifiers are not supported yet but will be supported eventually.
                ThrowModifierNotSupported();
            }
            else
            {
                ThrowModifierNotSupported();
            }
        }

        void ISearchValueVisitor.Visit(UriSearchValue uri)
        {
            EnsureArg.IsNotNull(uri, nameof(uri));

            switch (_modifier)
            {
                case null:
                    _outputExpression = Expression.Equals(FieldName.Uri, uri.Uri);
                    break;
                case SearchModifierCode.Above:
                    _outputExpression = Expression.And(
                        Expression.EndsWith(FieldName.Uri, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, KnownUriSchemes.Urn, false));
                    break;
                case SearchModifierCode.Below:
                    _outputExpression = Expression.And(
                        Expression.StartsWith(FieldName.Uri, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, KnownUriSchemes.Urn, false));
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
                case SearchComparator.Eq:
                case SearchComparator.Ap:
                    return Expression.And(
                        Expression.GreaterThanOrEqual(fieldName, lowerBound),
                        Expression.LessThanOrEqual(fieldName, upperBound));
                case SearchComparator.Ne:
                    return Expression.Or(
                        Expression.LessThan(fieldName, lowerBound),
                        Expression.GreaterThan(fieldName, upperBound));
                case SearchComparator.Ge:
                    return Expression.GreaterThanOrEqual(fieldName, number);
                case SearchComparator.Gt:
                case SearchComparator.Sa:
                    return Expression.GreaterThan(fieldName, number);
                case SearchComparator.Le:
                    return Expression.LessThanOrEqual(fieldName, number);
                case SearchComparator.Lt:
                case SearchComparator.Eb:
                    return Expression.LessThan(fieldName, number);
                default:
                    ThrowComparatorNotSupported();
                    break;
            }

            return null;
        }
    }
}
