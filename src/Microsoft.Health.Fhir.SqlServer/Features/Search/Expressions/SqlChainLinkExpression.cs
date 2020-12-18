// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    internal class SqlChainLinkExpression : Expression
    {
        public SqlChainLinkExpression(
            string resourceType,
            SearchParameterInfo referenceSearchParameter,
            string targetResourceType,
            bool reversed,
            Expression expressionOnSource = null,
            Expression expressionOnTarget = null)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsNotEmptyOrWhiteSpace(targetResourceType, nameof(targetResourceType));

            ResourceType = resourceType;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceType = targetResourceType;
            Reversed = reversed;
            ExpressionOnSource = expressionOnSource;
            ExpressionOnTarget = expressionOnTarget;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        public string TargetResourceType { get; }

        /// <summary>
        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        public Expression ExpressionOnSource { get; }

        public Expression ExpressionOnTarget { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return ((ISqlExpressionVisitor<TContext, TOutput>)visitor).VisitSqlChainLink(this, context);
        }

        public override string ToString()
        {
            return $"({(Reversed ? "Reverse " : string.Empty)}SqlChainLink {ReferenceSearchParameter.Name}:{TargetResourceType} {(ExpressionOnSource == null ? string.Empty : $" Source:{ExpressionOnSource}")}{(ExpressionOnTarget == null ? string.Empty : $" Target:{ExpressionOnTarget}")})";
        }
    }
}
