// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Returns the <see cref="TableExpressionQueryGenerator"/> for an expression.
    /// </summary>
    internal class TableExpressionQueryGeneratorFactory : IExpressionVisitorWithInitialContext<object, TableExpressionQueryGenerator>
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterToSearchValueTypeMap;
        private readonly ConcurrentDictionary<Uri, TableExpressionQueryGenerator> _cache = new ConcurrentDictionary<Uri, TableExpressionQueryGenerator>();

        public TableExpressionQueryGeneratorFactory(SearchParameterToSearchValueTypeMap searchParameterToSearchValueTypeMap)
        {
            EnsureArg.IsNotNull(searchParameterToSearchValueTypeMap, nameof(searchParameterToSearchValueTypeMap));
            _searchParameterToSearchValueTypeMap = searchParameterToSearchValueTypeMap;
        }

        public object InitialContext => null;

        public TableExpressionQueryGenerator VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, expression.Expression, context);
        }

        public TableExpressionQueryGenerator VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            return VisitSearchParameterExpressionBase(expression.Parameter, null, context);
        }

        private TableExpressionQueryGenerator VisitSearchParameterExpressionBase(SearchParameterInfo searchParameterInfo, Expression childExpression, object context)
        {
            if (searchParameterInfo.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
            {
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

            TableExpressionQueryGenerator GetGenerator(SearchParameterInfo param)
            {
                switch (param.Type)
                {
                    case SearchParamType.Token:
                        return TokenQueryGenerator.Instance;
                    case SearchParamType.Date:
                        return DateTimeQueryGenerator.Instance;
                    case SearchParamType.Number:
                        return NumberQueryGenerator.Instance;
                    case SearchParamType.Quantity:
                        return QuantityQueryGenerator.Instance;
                    case SearchParamType.Reference:
                        return ReferenceQueryGenerator.Instance;
                    case SearchParamType.String:
                        return StringQueryGenerator.Instance;
                    case SearchParamType.Uri:
                        return UriQueryGenerator.Instance;
                    case SearchParamType.Composite:
                        Type searchValueType = _searchParameterToSearchValueTypeMap.GetSearchValueType(param);
                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, QuantitySearchValue>))
                        {
                            return TokenQuantityCompositeQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<ReferenceSearchValue, TokenSearchValue>))
                        {
                            return ReferenceTokenCompositeQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, TokenSearchValue>))
                        {
                            return TokenTokenCompositeQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, DateTimeSearchValue>))
                        {
                            return TokenDateTimeCompositeQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, StringSearchValue>))
                        {
                            return TokenStringCompositeQueryGenerator.Instance;
                        }

                        if (searchValueType == typeof(ValueTuple<TokenSearchValue, NumberSearchValue, NumberSearchValue>))
                        {
                            return TokenNumberNumberQueryGenerator.Instance;
                        }

                        throw new InvalidOperationException($"Unexpected composite search parameter {param.Url}");

                    default:
                        throw new InvalidOperationException($"Unexpected search parameter type {param.Type}");
                }
            }
        }

        public TableExpressionQueryGenerator GetTableExpressionQueryGenerator(SearchParameterInfo searchParameterInfo)
        {
            return VisitSearchParameterExpressionBase(searchParameterInfo, null, null);
        }

        public TableExpressionQueryGenerator VisitBinary(BinaryExpression expression, object context)
        {
            throw new InvalidOperationException("Not expecting a BinaryExpression under a Token search param.");
        }

        public TableExpressionQueryGenerator VisitChained(ChainedExpression expression, object context)
        {
            return ChainLinkQueryGenerator.Instance;
        }

        public TableExpressionQueryGenerator VisitMissingField(MissingFieldExpression expression, object context)
        {
            return TokenQueryGenerator.Instance;
        }

        public TableExpressionQueryGenerator VisitMultiary(MultiaryExpression expression, object context)
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

        public TableExpressionQueryGenerator VisitString(StringExpression expression, object context)
        {
            if (expression.FieldName == FieldName.TokenText)
            {
                return TokenTextQueryGenerator.Instance;
            }

            return TokenQueryGenerator.Instance;
        }

        public TableExpressionQueryGenerator VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            return CompartmentQueryGenerator.Instance;
        }

        public TableExpressionQueryGenerator VisitInclude(IncludeExpression expression, object context)
        {
            return IncludeQueryGenerator.Instance;
        }

        public TableExpressionQueryGenerator VisitSortParameter(SortExpression expression, object context)
        {
            return GetTableExpressionQueryGenerator(expression.Parameter);
        }
    }
}
