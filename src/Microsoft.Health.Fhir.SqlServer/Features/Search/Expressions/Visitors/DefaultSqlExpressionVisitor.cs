// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal abstract class DefaultSqlExpressionVisitor<TContext, TOutput> : DefaultExpressionVisitor<TContext, TOutput>, ISqlExpressionVisitor<TContext, TOutput>
    {
        protected DefaultSqlExpressionVisitor()
        {
        }

        protected DefaultSqlExpressionVisitor(Func<TOutput, TOutput, TOutput> outputAggregator)
            : base(outputAggregator)
        {
        }

        public virtual TOutput VisitSqlRoot(SqlRootExpression expression, TContext context) => default;

        public virtual TOutput VisitTable(SearchParamTableExpression searchParamTableExpression, TContext context) => default;

        public virtual TOutput VisitSqlChainLink(SqlChainLinkExpression sqlChainLinkExpression, TContext context) => default;
    }
}
