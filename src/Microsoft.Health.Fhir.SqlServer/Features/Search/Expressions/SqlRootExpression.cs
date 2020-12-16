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
    /// <summary>
    /// The root of a search expression tree that will be translated to a SQL command.
    /// It is organized as a set of "normalized" table expression predicates and a set
    /// of "denormalized" predicates. The normalized predicates are over a search parameter
    /// table, whereas denormalized predicates are applied to the Resource table directly. Some
    /// of them can be applied to search parameter tables as well.
    /// </summary>
    internal class SqlRootExpression : Expression
    {
        public SqlRootExpression(IReadOnlyList<TableExpression> tableExpressions, IReadOnlyList<SearchParameterExpressionBase> resourceExpressions)
        {
            EnsureArg.IsNotNull(tableExpressions, nameof(tableExpressions));
            EnsureArg.IsNotNull(resourceExpressions, nameof(resourceExpressions));

            TableExpressions = tableExpressions;
            ResourceExpressions = resourceExpressions;
        }

        public IReadOnlyList<TableExpression> TableExpressions { get; }

        public IReadOnlyList<SearchParameterExpressionBase> ResourceExpressions { get; }

        public static SqlRootExpression WithTableExpressions(params TableExpression[] expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<SearchParameterExpressionBase>());
        }

        public static SqlRootExpression WithResourceExpressions(params SearchParameterExpressionBase[] expressions)
        {
            return new SqlRootExpression(Array.Empty<TableExpression>(), expressions);
        }

        public static SqlRootExpression WithTableExpressions(IReadOnlyList<TableExpression> expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<SearchParameterExpressionBase>());
        }

        public static SqlRootExpression WithResourceExpressions(IReadOnlyList<SearchParameterExpressionBase> expressions)
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
            return $"(SqlRoot (Joined{(TableExpressions.Any() ? " " + string.Join(" ", TableExpressions) : null)}) (Resource{(ResourceExpressions.Any() ? " " + string.Join(" ", ResourceExpressions) : null)}))";
        }
    }
}
