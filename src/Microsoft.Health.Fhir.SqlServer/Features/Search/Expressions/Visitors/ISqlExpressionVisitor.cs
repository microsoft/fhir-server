﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal interface ISqlExpressionVisitor<in TContext, out TOutput> : IExpressionVisitor<TContext, TOutput>
    {
        TOutput VisitSqlRoot(SqlRootExpression expression, TContext context);

        TOutput VisitTable(SearchParamTableExpression searchParamTableExpression, TContext context);

        TOutput VisitSqlChainLink(SqlChainLinkExpression sqlChainLinkExpression, TContext context);

        new TOutput VisitTrustedResourceIdList(TrustedResourceIdListExpression expression, TContext context);
    }
}
