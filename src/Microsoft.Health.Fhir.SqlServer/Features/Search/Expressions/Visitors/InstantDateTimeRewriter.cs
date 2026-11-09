// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal sealed class InstantDateTimeRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (!IsInstant(expression.Parameter))
            {
                return expression;
            }

            Expression predicate = expression.Expression.AcceptVisitor(EndFieldRewriter.Instance);
            return ReferenceEquals(predicate, expression.Expression)
                ? expression
                : new SearchParameterExpression(expression.Parameter, predicate);
        }

        public override Expression VisitTable(SearchParamTableExpression expression, object context)
        {
            Expression predicate = expression.Predicate?.AcceptVisitor(this, context);
            return ReferenceEquals(predicate, expression.Predicate)
                ? expression
                : new SearchParamTableExpression(expression.QueryGenerator, predicate, expression.Kind, expression.ChainLevel);
        }

        private static bool IsInstant(SearchParameterInfo parameter)
        {
            // Custom definitions can leave old index rows and stale definition caches on other instances.
            // Homogeneous unions have a known type, but union rewriting is not enabled.
            return parameter.IsSystemDefined &&
                parameter.Type == SearchParamType.Date &&
                parameter.TemporalType == TemporalType.Instant &&
                !parameter.Expression.Contains('|', StringComparison.Ordinal);
        }

        private sealed class EndFieldRewriter : SqlExpressionRewriterWithInitialContext<object>
        {
            public static readonly EndFieldRewriter Instance = new EndFieldRewriter();

            public override Expression VisitBinary(BinaryExpression expression, object context) =>
                expression.FieldName == FieldName.DateTimeEnd
                    ? new BinaryExpression(expression.BinaryOperator, FieldName.DateTimeStart, expression.ComponentIndex, expression.Value)
                    : expression;

            public override Expression VisitSearchParameter(SearchParameterExpression expression, object context) => expression;

            public override Expression VisitChained(ChainedExpression expression, object context) => expression;

            public override Expression VisitSqlChainLink(SqlChainLinkExpression expression, object context) => expression;

            public override Expression VisitTable(SearchParamTableExpression expression, object context) => expression;
        }
    }
}
