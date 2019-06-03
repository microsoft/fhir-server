// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites expressions over string search parameters to account for entries where the TextOverflow
    /// column is not null.
    /// </summary>
    internal class StringOverflowRewriter : ConcatenationRewriter
    {
        internal static readonly StringOverflowRewriter Instance = new StringOverflowRewriter();

        private StringOverflowRewriter()
            : base(new Scout())
        {
        }

        public override Expression VisitString(StringExpression expression, object context)
        {
            return new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase);
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            internal Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitString(StringExpression expression, object context)
            {
                switch (expression.StringOperator)
                {
                    case StringOperator.Equals:
                    case StringOperator.NotStartsWith:
                    case StringOperator.StartsWith:
                        if (expression.Value.Length < V1.StringSearchParam.Text.Metadata.MaxLength / 2)
                        {
                            return false;
                        }

                        return true;

                    default:
                        return true;
                }
            }
        }
    }
}
