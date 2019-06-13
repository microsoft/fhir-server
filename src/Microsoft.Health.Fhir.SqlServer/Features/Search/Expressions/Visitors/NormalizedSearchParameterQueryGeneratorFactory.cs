// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Returns the <see cref="NormalizedSearchParameterQueryGenerator"/> for an expression.
    /// </summary>
    internal class NormalizedSearchParameterQueryGeneratorFactory : IExpressionVisitorWithInitialContext<object, NormalizedSearchParameterQueryGenerator>
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterToSearchValueTypeMap;
        private readonly ConcurrentDictionary<Uri, NormalizedSearchParameterQueryGenerator> _cache = new ConcurrentDictionary<Uri, NormalizedSearchParameterQueryGenerator>();

        public NormalizedSearchParameterQueryGeneratorFactory(SearchParameterToSearchValueTypeMap searchParameterToSearchValueTypeMap)
        {
            EnsureArg.IsNotNull(searchParameterToSearchValueTypeMap, nameof(searchParameterToSearchValueTypeMap));
            _searchParameterToSearchValueTypeMap = searchParameterToSearchValueTypeMap;
        }

        public object InitialContext => null;

        public NormalizedSearchParameterQueryGenerator VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, expression.Expression, context);
        }

        public NormalizedSearchParameterQueryGenerator VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, null, context);
        }

        private NormalizedSearchParameterQueryGenerator VisitSearchParameterExpressionBase(SearchParameterInfo searchParameterInfo, Expression childExpression, object context)
        {
            switch (searchParameterInfo.Name)
            {
                case SearchParameterNames.Id:
                case SearchParameterNames.LastUpdated:
                case SearchParameterNames.ResourceType:
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    // these are all denormalized
                    return null;
            }

            if (childExpression != null)
            {
                if (searchParameterInfo.Type == SearchParamType.Token)
                {
                    // could be Token or TokenText
                    return childExpression.AcceptVisitor(this, context);
                }
            }

            if (!_cache.TryGetValue(searchParameterInfo.Url, out var generator))
            {
                generator = GetGenerator(searchParameterInfo);
                _cache.TryAdd(searchParameterInfo.Url, generator);
            }

            return generator;

            NormalizedSearchParameterQueryGenerator GetGenerator(SearchParameterInfo param)
            {
                switch (param.Type)
                {
                    case SearchParamType.Token:
                        return TokenSearchParameterQueryGenerator.Instance;
                    case SearchParamType.Date:
                        return DateTimeSearchParameterQueryGenerator.Instance;
                    case SearchParamType.Number:
                        return NumberSearchParameterQueryGenerator.Instance;
                    case SearchParamType.Quantity:
                        return QuantitySearchParameterQueryGenerator.Instance;
                    case SearchParamType.Reference:
                        return ReferenceSearchParameterQueryGenerator.Instance;
                    case SearchParamType.String:
                        return StringSearchParameterQueryGenerator.Instance;
                    case SearchParamType.Uri:
                        return UriSearchParameterQueryGenerator.Instance;
                    case SearchParamType.Composite:
                        Type searchValueType = _searchParameterToSearchValueTypeMap.GetSearchValueType(param);
                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, QuantitySearchValue>))
                        {
                            return TokenQuantityCompositeSearchParameterQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<ReferenceSearchValue, TokenSearchValue>))
                        {
                            return ReferenceTokenCompositeSearchParameterQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, TokenSearchValue>))
                        {
                            return TokenTokenCompositeSearchParameterQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, DateTimeSearchValue>))
                        {
                            return TokenDateTimeCompositeSearchParameterQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, StringSearchValue>))
                        {
                            return TokenStringCompositeSearchParameterQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, NumberSearchValue, NumberSearchValue>))
                        {
                            return TokenNumberNumberSearchParameterQueryGenerator.Instance;
                        }

                        throw new InvalidOperationException($"Unexpected composite search parameter {param.Url}");

                    default:
                        throw new InvalidOperationException($"Unexpected search parameter type {param.Type}");
                }
            }
        }

        public NormalizedSearchParameterQueryGenerator VisitBinary(BinaryExpression expression, object context)
        {
            throw new InvalidOperationException("Not expecting a BinaryExpression under a Token search param.");
        }

        public NormalizedSearchParameterQueryGenerator VisitChained(ChainedExpression expression, object context)
        {
            return ChainAnchorQueryGenerator.Instance;
        }

        public NormalizedSearchParameterQueryGenerator VisitMissingField(MissingFieldExpression expression, object context)
        {
            return TokenSearchParameterQueryGenerator.Instance;
        }

        public NormalizedSearchParameterQueryGenerator VisitMultiary(MultiaryExpression expression, object context)
        {
            foreach (var childExpression in expression.Expressions)
            {
                var handler = childExpression.AcceptVisitor(this, context);
                if (handler != null)
                {
                    return handler;
                }
            }

            return null;
        }

        public NormalizedSearchParameterQueryGenerator VisitString(StringExpression expression, object context)
        {
            if (expression.FieldName == FieldName.TokenText)
            {
                return TokenTextSearchParameterQueryGenerator.Instance;
            }

            return TokenSearchParameterQueryGenerator.Instance;
        }

        public NormalizedSearchParameterQueryGenerator VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            return CompartmentSearchParameterQueryGenerator.Instance;
        }
    }
}
