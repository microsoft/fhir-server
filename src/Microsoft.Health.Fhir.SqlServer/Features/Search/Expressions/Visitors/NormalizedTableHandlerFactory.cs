// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.NormalizedTableHandlers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class NormalizedTableHandlerFactory : IExpressionVisitorWithInitialContext<object, NormalizedTableHandler>
    {
        private ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterToSearchValueTypeMap;
        private readonly ConcurrentDictionary<Uri, NormalizedTableHandler> _cache = new ConcurrentDictionary<Uri, NormalizedTableHandler>();

        public NormalizedTableHandlerFactory(ISearchParameterDefinitionManager searchParameterDefinitionManager, SearchParameterToSearchValueTypeMap searchParameterToSearchValueTypeMap)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterToSearchValueTypeMap, nameof(searchParameterToSearchValueTypeMap));
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterToSearchValueTypeMap = searchParameterToSearchValueTypeMap;
        }

        public object InitialContext => null;

        public NormalizedTableHandler VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, expression.Expression, context);
        }

        public NormalizedTableHandler VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, null, context);
        }

        private NormalizedTableHandler VisitSearchParameterExpressionBase(SearchParameterInfo searchParameterInfo, Expression childExpression, object context)
        {
            switch (searchParameterInfo.Name)
            {
                case SearchParameterNames.Id:
                case SearchParameterNames.LastUpdated:
                case SearchParameterNames.ResourceType:
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
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

            if (!_cache.TryGetValue(searchParameterInfo.Url, out var handler))
            {
                handler = NormalizedTableHandler(searchParameterInfo);
                _cache.TryAdd(searchParameterInfo.Url, handler);
            }

            return handler;

            NormalizedTableHandler NormalizedTableHandler(SearchParameterInfo param)
            {
                switch (param.Type)
                {
                    case SearchParamType.Token:
                        return TokenNormalizedTableHandler.Instance;
                    case SearchParamType.Date:
                        return DateTimeNormalizedTableHandler.Instance;
                    case SearchParamType.Number:
                        return NumberNormalizedTableHandler.Instance;
                    case SearchParamType.Quantity:
                        return QuantityNormalizedTableHandler.Instance;
                    case SearchParamType.Reference:
                        return ReferenceNormalizedTableHandler.Instance;
                    case SearchParamType.Str:
                        return StringNormalizedTableHandler.Instance;
                    case SearchParamType.Uri:
                        return UriNormalizedTableHandler.Instance;
                    case SearchParamType.Composite:
                        Type searchValueType = _searchParameterToSearchValueTypeMap.GetSearchValueType(param);
                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, QuantitySearchValue>))
                        {
                            return TokenQuantityCompositeNormalizedTableHandler.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<ReferenceSearchValue, TokenSearchValue>))
                        {
                            return ReferenceTokenCompositeNormalizedTableHandler.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, TokenSearchValue>))
                        {
                            return TokenTokenCompositeNormalizedTableHandler.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, DateTimeSearchValue>))
                        {
                            return TokenDateTimeCompositeNormalizedTableHandler.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, StringSearchValue>))
                        {
                            return TokenStringCompositeNormalizedTableHandler.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, NumberSearchValue, NumberSearchValue>))
                        {
                            return TokenNumberNumberNormalizedTableHandler.Instance;
                        }

                        throw new InvalidOperationException($"Unexpected composite search parameter {param.Url}");

                    default:
                        throw new InvalidOperationException($"Unexpected search parameter type {param.Type}");
                }
            }
        }

        public NormalizedTableHandler VisitBinary(BinaryExpression expression, object context)
        {
            throw new InvalidOperationException("Should not get here");
        }

        public NormalizedTableHandler VisitChained(ChainedExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public NormalizedTableHandler VisitMissingField(MissingFieldExpression expression, object context)
        {
            return TokenNormalizedTableHandler.Instance;
        }

        public NormalizedTableHandler VisitMultiary(MultiaryExpression expression, object context)
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

        public NormalizedTableHandler VisitString(StringExpression expression, object context)
        {
            if (expression.FieldName == FieldName.TokenText)
            {
                return TokenTextNormalizedTableHandler.Instance;
            }

            return TokenNormalizedTableHandler.Instance;
        }

        public NormalizedTableHandler VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            return CompartmentNormalizedTableHandler.Instance;
        }
    }
}
