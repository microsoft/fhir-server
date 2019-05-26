// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    internal class SqlRootExpression : Expression
    {
        public SqlRootExpression(IReadOnlyList<TableExpression> normalizedPredicates, IReadOnlyList<Expression> denormalizedPredicates)
        {
            EnsureArg.IsNotNull(normalizedPredicates, nameof(normalizedPredicates));
            EnsureArg.IsNotNull(denormalizedPredicates, nameof(denormalizedPredicates));

            NormalizedPredicates = normalizedPredicates;
            DenormalizedPredicates = denormalizedPredicates;
        }

        public IReadOnlyList<TableExpression> NormalizedPredicates { get; }

        public IReadOnlyList<Expression> DenormalizedPredicates { get; }

        public static SqlRootExpression WithNormalizedPredicates(params TableExpression[] expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<Expression>());
        }

        public static SqlRootExpression WithDenormalizedPredicates(params Expression[] expressions)
        {
            return new SqlRootExpression(Array.Empty<TableExpression>(), expressions);
        }

        public static SqlRootExpression WithNormalizedPredicates(IReadOnlyList<TableExpression> expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<Expression>());
        }

        public static SqlRootExpression WithDenormalizedPredicates(IReadOnlyList<Expression> expressions)
        {
            return new SqlRootExpression(Array.Empty<TableExpression>(), expressions);
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return AcceptVisitor((ISqlExpressionVisitor<TContext, TOutput>)visitor, context);
        }

        public TOutput AcceptVisitor<TContext, TOutput>(ISqlExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSqlRoot(this, context);
        }

        public override string ToString()
        {
            return $"(SqlRoot (Joined{(NormalizedPredicates.Any() ? " " + string.Join(" ", NormalizedPredicates) : null)}) (Resource{(DenormalizedPredicates.Any() ? " " + string.Join(" ", DenormalizedPredicates) : null)}))";
        }
    }
}
