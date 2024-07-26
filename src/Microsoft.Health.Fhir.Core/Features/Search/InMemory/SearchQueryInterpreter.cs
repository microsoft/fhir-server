// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.InMemory
{
    public delegate IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> SearchPredicate(IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> input);

    internal class SearchQueryInterpreter : IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
    {
        Context IExpressionVisitorWithInitialContext<Context, SearchPredicate>.InitialContext => default;

        public SearchPredicate VisitSearchParameter(SearchParameterExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            return VisitInnerWithContext(expression.Parameter.Name, expression.Expression, context);
        }

        public SearchPredicate VisitBinary(BinaryExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            return VisitBinary(context.ParameterName, expression.BinaryOperator, expression.Value);
        }

        private static SearchPredicate VisitBinary(string fieldName, BinaryOperator op, object value)
        {
            EnsureArg.IsNotNull(fieldName, nameof(fieldName));
            EnsureArg.IsNotNull(value, nameof(value));

            return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == fieldName && GetMappedValue(op, y.Value, (IComparable)value)));
        }

        private static bool GetMappedValue(BinaryOperator expressionBinaryOperator, ISearchValue first, IComparable second)
        {
            EnsureArg.IsNotNull(first, nameof(first));
            EnsureArg.IsNotNull(second, nameof(second));

            if (first == null || second == null)
            {
                return false;
            }

            var comparisonVisitor = new ComparisonValueVisitor(expressionBinaryOperator, second);
            first.AcceptVisitor(comparisonVisitor);

            return comparisonVisitor.Compare();
        }

        public SearchPredicate VisitChained(ChainedExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new SearchOperationNotSupportedException("ChainedExpression is not supported.");
        }

        public SearchPredicate VisitMissingField(MissingFieldExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitMissingSearchParameter(MissingSearchParameterExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitMultiary(MultiaryExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull(expression.Expressions, nameof(expression.Expressions));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            return expression.Expressions.Select(x => x.AcceptVisitor(this, context))
                    .Aggregate((x, y) =>
                    {
                        switch (expression.MultiaryOperation)
                        {
                            case MultiaryOperator.And:
                                return p => x(p).Intersect(y(p));
                            case MultiaryOperator.Or:
                                return p => x(p).Union(y(p));
                            default:
                                throw new NotImplementedException();
                        }
                    });
        }

        public SearchPredicate VisitString(StringExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            StringComparison comparison = expression.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (context.ParameterName == "_type")
            {
                return input => input.Where(x => x.Location.ResourceType.Equals(expression.Value, comparison));
            }
            else
            {
                switch (expression.StringOperator)
                {
                    case StringOperator.StartsWith:
                        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == context.ParameterName &&
                                                                          CompareStringParameter(y, (a, b, c) => a.StartsWith(b, c))));
                    case StringOperator.Equals:
                        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == context.ParameterName &&
                                                                          CompareStringParameter(y, string.Equals)));

                    default:
                        throw new NotImplementedException();
                }
            }

            bool CompareStringParameter(SearchIndexEntry y, Func<string, string, StringComparison, bool> compareFunc)
            {
                EnsureArg.IsNotNull(y, nameof(y));

                switch (y.SearchParameter.Type)
                {
                    case ValueSets.SearchParamType.String:
                        return compareFunc(((StringSearchValue)y.Value).String, expression.Value, comparison);

                    case ValueSets.SearchParamType.Token:
                        return compareFunc(((TokenSearchValue)y.Value).Code, expression.Value, comparison) ||
                               compareFunc(((TokenSearchValue)y.Value).System, expression.Value, comparison);
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public SearchPredicate VisitCompartment(CompartmentSearchExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new SearchOperationNotSupportedException("Compartment search is not supported.");
        }

        public SearchPredicate VisitInclude(IncludeExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        private SearchPredicate VisitInnerWithContext(string parameterName, Expression expression, Context context, bool negate = false)
        {
            EnsureArg.IsNotNull(parameterName, nameof(parameterName));
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            var newContext = context.WithParameterName(parameterName);

            SearchPredicate filter = input =>
            {
                if (expression != null)
                {
                    return expression.AcceptVisitor(this, newContext)(input);
                }
                else
                {
                    // :missing will end up here
                    throw new NotSupportedException("This query is not supported");
                }
            };

            if (negate)
            {
                SearchPredicate inner = filter;
                filter = input => input.Except(inner(input));
            }

            return filter;
        }

        public SearchPredicate VisitNotExpression(NotExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitUnion(UnionExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitSmartCompartment(SmartCompartmentSearchExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitSortParameter(SortExpression expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            throw new NotImplementedException();
        }

        public SearchPredicate VisitIn<T>(InExpression<T> expression, Context context)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull<Context>(context, nameof(context));

            return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == context.ParameterName &&
                                                              expression.Values.Contains((T)y.Value)));
        }

        /// <summary>
        /// Context that is passed through the visit.
        /// </summary>
        internal struct Context
        {
            public string ParameterName { get; set; }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Internal API")]
            public Context WithParameterName(string paramName)
            {
                return new Context
                {
                    ParameterName = paramName,
                };
            }
        }
    }
}
