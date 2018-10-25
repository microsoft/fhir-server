// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    public class LegacySearchValueExpressionBuilder : ILegacySearchValueExpressionBuilder, ISearchValueVisitor
    {
        // TODO: Make the approximate datetime range multiplier available as a setting to tenants
        private const double ApproximateDateTimeRangeMultiplier = .1;
        private SearchParam _searchParam;
        private SearchModifierCode? _modifier;
        private SearchComparator _comparator;

        private Expression _outputExpression;

        public Expression Build(
            SearchParam searchParam,
            SearchModifierCode? modifier,
            SearchComparator comparator,
            string value)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));
            Debug.Assert(
                modifier == null || Enum.IsDefined(typeof(SearchModifierCode), modifier.Value),
                "Invalid modifier.");
            Debug.Assert(
                Enum.IsDefined(typeof(SearchComparator), comparator),
                "Invalid SearchComparator.");
            EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

            _searchParam = searchParam;
            _modifier = modifier;
            _comparator = comparator;

            if (modifier == SearchModifierCode.Missing)
            {
                // We have to handle :missing modifier specially because if :missing modifier is specified,
                // then the value is a boolean string indicating whether the parameter is missing or not instead of
                // the search value type associated with the search parameter.
                if (!bool.TryParse(value, out bool isMissing))
                {
                    // An invalid value was specified.
                    throw new InvalidSearchOperationException(Core.Resources.InvalidValueTypeForMissingModifier);
                }

                return Expression.MissingSearchParameter(searchParam.ParamName, isMissing);
            }
            else if (modifier == SearchModifierCode.Text)
            {
                // We have to handle :text modifier specially because if :text modifier is supplied for token search param,
                // then we want to search the display text using the specified text, and therefore
                // we don't want to actually parse the specified text into token.
                if (searchParam.ParamType != SearchParamType.Token)
                {
                    ThrowModifierNotSupported();
                }

                _outputExpression = Expression.Contains(FieldName.TokenText, null, value, true);
            }
            else
            {
                // Build the expression for based on the search value.
                ISearchValue searchValue = searchParam.Parse(value);

                searchValue.AcceptVisitor(this);
            }

            // Add the parameter name matching expression which is common to all search values.
            _outputExpression = Expression.And(
                Expression.Equals(FieldName.ParamName, null, searchParam.ParamName),
                _outputExpression);

            return _outputExpression;
        }

        void ISearchValueVisitor.Visit(LegacyCompositeSearchValue composite)
        {
            EnsureArg.IsNotNull(composite, nameof(composite));

            throw new SearchOperationNotSupportedException("Composite search parameter is not supported.");
        }

        void ISearchValueVisitor.Visit(CompositeSearchValue composite)
        {
            EnsureArg.IsNotNull(composite, nameof(composite));

            throw new SearchOperationNotSupportedException("Composite search parameter is not supported.");
        }

        void ISearchValueVisitor.Visit(DateTimeSearchValue dateTime)
        {
            EnsureArg.IsNotNull(dateTime, nameof(dateTime));

            // Based on spec here: http://hl7.org/fhir/search.html#prefix
            switch (_comparator)
            {
                case SearchComparator.Eq:
                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, dateTime.Start),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, dateTime.End));
                    break;
                case SearchComparator.Ne:
                    _outputExpression = Expression.Or(
                        Expression.LessThan(FieldName.DateTimeStart, null, dateTime.Start),
                        Expression.GreaterThan(FieldName.DateTimeEnd, null, dateTime.End));
                    break;
                case SearchComparator.Lt:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeStart, null, dateTime.Start);
                    break;
                case SearchComparator.Gt:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeEnd, null, dateTime.End);
                    break;
                case SearchComparator.Le:
                    _outputExpression = Expression.LessThanOrEqual(FieldName.DateTimeStart, null, dateTime.End);
                    break;
                case SearchComparator.Ge:
                    _outputExpression = Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, dateTime.Start);
                    break;
                case SearchComparator.Sa:
                    _outputExpression = Expression.GreaterThan(FieldName.DateTimeStart, null, dateTime.End);
                    break;
                case SearchComparator.Eb:
                    _outputExpression = Expression.LessThan(FieldName.DateTimeEnd, null, dateTime.Start);
                    break;
                case SearchComparator.Ap:
                    var startTicks = dateTime.Start.UtcTicks;
                    var endTicks = dateTime.End.UtcTicks;

                    var differenceTicks = (long)((Clock.UtcNow.Ticks - Math.Max(startTicks, endTicks)) * ApproximateDateTimeRangeMultiplier);

                    var approximateStart = dateTime.Start.AddTicks(-differenceTicks);
                    var approximateEnd = dateTime.End.AddTicks(differenceTicks);

                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, approximateStart),
                        Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, approximateEnd));
                    break;
                default:
                    ThrowComparatorNotSupported();
                    break;
            }
        }

        void ISearchValueVisitor.Visit(NumberSearchValue number)
        {
            EnsureArg.IsNotNull(number, nameof(number));

            var modifierDecimal = number.Number.GetPrescisionModifier();

            var lowerBound = number.Number - modifierDecimal;
            var upperBound = number.Number + modifierDecimal;

            switch (_comparator)
            {
                case SearchComparator.Eq:
                case SearchComparator.Ap:
                    _outputExpression = Expression.And(
                        Expression.GreaterThanOrEqual(FieldName.Number, null, lowerBound),
                        Expression.LessThanOrEqual(FieldName.Number, null, upperBound));
                    break;
                case SearchComparator.Ne:
                    _outputExpression = Expression.Or(
                        Expression.GreaterThan(FieldName.Number, null, upperBound),
                        Expression.LessThan(FieldName.Number, null, lowerBound));
                    break;
                case SearchComparator.Ge:
                    _outputExpression = Expression.GreaterThanOrEqual(FieldName.Number, null, number.Number);
                    break;
                case SearchComparator.Gt:
                case SearchComparator.Sa:
                    _outputExpression = Expression.GreaterThan(FieldName.Number, null, number.Number);
                    break;
                case SearchComparator.Le:
                    _outputExpression = Expression.LessThanOrEqual(FieldName.Number, null, number.Number);
                    break;
                case SearchComparator.Lt:
                case SearchComparator.Eb:
                    _outputExpression = Expression.LessThan(FieldName.Number, null, number.Number);
                    break;
                default:
                    ThrowComparatorNotSupported();
                    break;
            }
        }

        void ISearchValueVisitor.Visit(QuantitySearchValue quantity)
        {
            EnsureArg.IsNotNull(quantity, nameof(quantity));

            throw new SearchOperationNotSupportedException("Quantity search parameter is not supported.");
        }

        void ISearchValueVisitor.Visit(ReferenceSearchValue reference)
        {
            EnsureArg.IsNotNull(reference, nameof(reference));

            EnsureOnlyEqualComparatorIsSupported();

            // TODO: For now, we will assume that the incoming references are all in relative URL.
            // User story 63437 will deal with full URL scenario.
            _outputExpression = Expression.StringEquals(FieldName.Reference, null, reference.Reference, false);
        }

        void ISearchValueVisitor.Visit(StringSearchValue s)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            EnsureOnlyEqualComparatorIsSupported();

            if (_modifier == null)
            {
                // Based on spec http://hl7.org/fhir/search.html#string,
                // is case-insensitive search so we will normalize into lower case for search.
                _outputExpression = Expression.StartsWith(FieldName.String, null, s.String, true);
            }
            else if (_modifier == SearchModifierCode.Exact)
            {
                _outputExpression = Expression.StringEquals(FieldName.String, null, s.String, false);
            }
            else if (_modifier == SearchModifierCode.Contains)
            {
                // Based on spec http://hl7.org/fhir/search.html#modifiers,
                // contains is case-insensitive search so we will normalize into lower case for search.
                _outputExpression = Expression.Contains(FieldName.String, null, s.String, true);
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
                // Based on spec http://hl7.org/fhir/search.html#token,
                // we need to make sure to test if system is missing or not based on how it is supplied.
                if (token.System == null)
                {
                    // If the system is not supplied, then the token code is matched irrespective of the value of system.
                    _outputExpression = Expression.StringEquals(FieldName.TokenCode, null, token.Code, false);
                }
                else if (token.System.Length == 0)
                {
                    // If the system is empty, then the token is matched if there is no system property.
                    _outputExpression = Expression.And(
                       Expression.Missing(FieldName.TokenSystem, null),
                       Expression.StringEquals(FieldName.TokenCode, null, token.Code, false));
                }
                else if (string.IsNullOrWhiteSpace(token.Code))
                {
                    // If the code is empty, then the token is matched if system is matched.
                    _outputExpression = Expression.StringEquals(FieldName.TokenSystem, null, token.System, false);
                }
                else
                {
                    _outputExpression = Expression.And(
                        Expression.StringEquals(FieldName.TokenSystem, null, token.System, false),
                        Expression.StringEquals(FieldName.TokenCode, null, token.Code, false));
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
                    _outputExpression = Expression.Equals(FieldName.Uri, null, uri.Uri);
                    break;
                case SearchModifierCode.Above:
                    _outputExpression = Expression.And(
                        Expression.EndsWith(FieldName.Uri, null, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, null, KnownUriSchemes.Urn, false));
                    break;
                case SearchModifierCode.Below:
                    _outputExpression = Expression.And(
                        Expression.StartsWith(FieldName.Uri, null, uri.Uri, false),
                        Expression.NotStartsWith(FieldName.Uri, null, KnownUriSchemes.Urn, false));
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
                string.Format(Core.Resources.ModifierNotSupported, _modifier, _searchParam.ParamName));
        }

        private void ThrowComparatorNotSupported()
        {
            throw new InvalidSearchOperationException(
                string.Format(Core.Resources.ComparatorNotSupported, _comparator, _searchParam.ParamName));
        }
    }
}
