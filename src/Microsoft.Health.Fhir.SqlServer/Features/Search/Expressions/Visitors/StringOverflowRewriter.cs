// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// To be used with schema versions greater than or equal to <see cref="SchemaVersionConstants.PartitionedTables"/>.
    /// Rewrites expressions over string search parameters to account for long entries that require use of the TextOverflow column.
    /// </summary>
    internal class StringOverflowRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly StringOverflowRewriter Instance = new();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            IReadOnlyList<SearchParamTableExpression> visitedTableExpressions = VisitArray(expression.SearchParamTableExpressions, context);

            if (ReferenceEquals(visitedTableExpressions, expression.SearchParamTableExpressions))
            {
                return expression;
            }

            return new SqlRootExpression(visitedTableExpressions, expression.ResourceTableExpressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Type == SearchParamType.String ||
                (expression.Parameter.Type == SearchParamType.Composite &&
                 expression.Parameter.Component.Any(c => c.ResolvedSearchParameter.Type == SearchParamType.String)))
            {
                return base.VisitSearchParameter(expression, expression.Parameter);
            }

            return expression;
        }

        public override Expression VisitString(StringExpression expression, object context)
        {
            if (expression.FieldName == FieldName.TokenCode)
            {
                return expression;
            }

            // TODO: We decided to do token differently and then go back and do string same way as token. No need for TokenOverflowRewritter.cs.

            switch (expression.StringOperator)
            {
                case StringOperator.Equals:
                case StringOperator.StartsWith:
                    if (expression.Value.Length <= VLatest.StringSearchParam.Text.Metadata.MaxLength)
                    {
                        // checking the Text column will be sufficient
                        return expression;
                    }

                    // We need to check the TextOverflow column. But we also check the Text column to allow an index seek
                    string prefix = expression.Value.Substring(0, (int)VLatest.StringSearchParam.Text.Metadata.MaxLength);

                    return Expression.And(
                        new StringExpression(expression.StringOperator, expression.FieldName, expression.ComponentIndex, prefix, expression.IgnoreCase),
                        new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase));

                case StringOperator.Contains:
                    // We need to consider the entire string, so we need to check TextOverflow if it is populated.
                    return Expression.Or(
                        expression,
                        new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase));
                default:
                    throw new InvalidOperationException($"Unexpected operator '{expression.StringOperator}' for string search parameter.");
            }
        }
    }
}
