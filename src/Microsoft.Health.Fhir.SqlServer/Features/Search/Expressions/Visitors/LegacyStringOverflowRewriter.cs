// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// To be used with schema versions less than <see cref="SchemaVersionConstants.PartitionedTables"/>.
    /// Rewrites expressions over string search parameters to account for entries where the TextOverflow
    /// column is not null.
    /// </summary>
    internal class LegacyStringOverflowRewriter : ConcatenationRewriter
    {
        public static readonly LegacyStringOverflowRewriter Instance = new LegacyStringOverflowRewriter();

        public LegacyStringOverflowRewriter()
            : base(new Scout())
        {
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Type == SearchParamType.String ||
                expression.Parameter.Type == SearchParamType.Composite)
            {
                return base.VisitSearchParameter(expression, expression.Parameter);
            }

            return expression;
        }

        public override Expression VisitString(StringExpression expression, object context)
        {
            var searchParameterInfo = (SearchParameterInfo)context;
            if ((expression.ComponentIndex == null ? searchParameterInfo.Type : searchParameterInfo.Component[expression.ComponentIndex.Value].ResolvedSearchParameter.Type) != SearchParamType.String)
            {
                return expression;
            }

            return new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase);
        }

        private class Scout : DefaultSqlExpressionVisitor<object, bool>
        {
            internal Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitSearchParameter(SearchParameterExpression expression, object context)
            {
                if (expression.Parameter.Type == SearchParamType.String ||
                    expression.Parameter.Type == SearchParamType.Composite)
                {
                    return expression.Expression.AcceptVisitor(this, expression.Parameter);
                }

                return false;
            }

            public override bool VisitString(StringExpression expression, object context)
            {
                var searchParameterInfo = (SearchParameterInfo)context;

                if ((expression.ComponentIndex == null ? searchParameterInfo.Type : searchParameterInfo.Component[expression.ComponentIndex.Value].ResolvedSearchParameter.Type) != SearchParamType.String)
                {
                    return false;
                }

                if (expression.StringOperator == StringOperator.Equals && expression.Value.Length <= VLatest.StringSearchParam.Text.Metadata.MaxLength)
                {
                    // in these cases, we will know for sure that we do not need to consider the overflow column
                    return false;
                }

                return true;
            }
        }
    }
}
