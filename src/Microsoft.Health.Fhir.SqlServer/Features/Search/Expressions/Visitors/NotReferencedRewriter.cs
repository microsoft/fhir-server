// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class NotReferencedRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly NotReferencedRewriter Instance = new NotReferencedRewriter();

        // private static readonly SearchParamTableExpression _notReferencedExpression = new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.NotReferenced);

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            return expression;
        }
    }
}
