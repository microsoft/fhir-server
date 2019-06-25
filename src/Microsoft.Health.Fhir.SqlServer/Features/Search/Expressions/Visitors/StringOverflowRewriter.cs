// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites expressions over string search parameters to account for entries where the TextOverflow
    /// column is not null.
    /// </summary>
    internal class StringOverflowRewriter : ConcatenationRewriter
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public StringOverflowRewriter(ISearchParameterDefinitionManager searchParameterDefinitionManager)
            : base(new Scout(searchParameterDefinitionManager))
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
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
            if (_searchParameterDefinitionManager.GetSearchParameterType((SearchParameterInfo)context, expression.ComponentIndex) != SearchParamType.String)
            {
                return expression;
            }

            return new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase);
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

            internal Scout(ISearchParameterDefinitionManager searchParameterDefinitionManager)
                : base((accumulated, current) => accumulated || current)
            {
                EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
                _searchParameterDefinitionManager = searchParameterDefinitionManager;
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
                if (_searchParameterDefinitionManager.GetSearchParameterType((SearchParameterInfo)context, expression.ComponentIndex) != SearchParamType.String)
                {
                    return false;
                }

                if (expression.StringOperator == StringOperator.Equals && expression.Value.Length <= V1.StringSearchParam.Text.Metadata.MaxLength)
                {
                    // in these cases, we will know for sure that we do not need to consider the overflow column
                    return false;
                }

                return true;
            }
        }
    }
}
