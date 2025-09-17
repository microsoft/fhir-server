﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// An expression type used to represent a non-leaf chaining expression. Used by the SQL layer, created from <see cref="ChainedExpression"/>.
    /// </summary>
    internal class SqlChainLinkExpression : Expression
    {
        public SqlChainLinkExpression(
            string[] resourceTypes,
            SearchParameterInfo referenceSearchParameter,
            string[] targetResourceTypes,
            bool reversed,
            Expression expressionOnSource = null,
            Expression expressionOnTarget = null)
        {
            EnsureArg.IsNotNull(resourceTypes, nameof(resourceTypes));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));

            ResourceTypes = resourceTypes;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceTypes = targetResourceTypes;
            Reversed = reversed;
            ExpressionOnSource = expressionOnSource;
            ExpressionOnTarget = expressionOnTarget;
        }

        /// <summary>
        /// Gets the resource types which are being searched.
        /// </summary>
        public string[] ResourceTypes { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource types.
        /// </summary>
        public string[] TargetResourceTypes { get; }

        /// <summary>
        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// The expression on the chain target. For example, for Observation?subject:Patient._lastUpdated=2020, this would be _lastUpdated=2020
        /// </summary>
        public Expression ExpressionOnTarget { get; }

        /// <summary>
        /// The expression on the chain source. For example, for Observation?subject:Patient._lastUpdated=2020, this would be type=Observation
        /// </summary>
        public Expression ExpressionOnSource { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return ((ISqlExpressionVisitor<TContext, TOutput>)visitor).VisitSqlChainLink(this, context);
        }

        public override string ToString()
        {
            return $"({(Reversed ? "Reverse " : string.Empty)}SqlChainLink {ReferenceSearchParameter.Code}:{string.Join(", ", TargetResourceTypes)} {(ExpressionOnSource == null ? string.Empty : $" Source:{ExpressionOnSource}")}{(ExpressionOnTarget == null ? string.Empty : $" Target:{ExpressionOnTarget}")})";
        }

        public override string GetExpressionParameterNames()
        {
            return $"({(Reversed ? "Reverse " : string.Empty)}SqlChainLink {ReferenceSearchParameter.Code}:{string.Join(", ", TargetResourceTypes)} {(ExpressionOnSource == null ? string.Empty : $" Source:{ExpressionOnSource.GetExpressionParameterNames()}")}{(ExpressionOnTarget == null ? string.Empty : $" Target:{ExpressionOnTarget.GetExpressionParameterNames()}")})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(SqlChainLinkExpression));
            foreach (string resourceType in ResourceTypes)
            {
                hashCode.Add(resourceType);
            }

            foreach (string targetResourceType in TargetResourceTypes)
            {
                hashCode.Add(targetResourceType);
            }

            hashCode.Add(ReferenceSearchParameter);

            hashCode.Add(Reversed);

            ExpressionOnSource?.AddValueInsensitiveHashCode(ref hashCode);
            ExpressionOnTarget?.AddValueInsensitiveHashCode(ref hashCode);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            if (other is not SqlChainLinkExpression chainLink ||
                chainLink.ResourceTypes.Length != ResourceTypes.Length ||
                chainLink.TargetResourceTypes.Length != TargetResourceTypes.Length ||
                !chainLink.ReferenceSearchParameter.Equals(ReferenceSearchParameter) ||
                chainLink.Reversed != Reversed ||
                !(chainLink.ExpressionOnSource?.ValueInsensitiveEquals(ExpressionOnSource) ?? ExpressionOnSource == null) ||
                !(chainLink.ExpressionOnTarget?.ValueInsensitiveEquals(ExpressionOnTarget) ?? ExpressionOnTarget == null))
            {
                return false;
            }

            for (var i = 0; i < ResourceTypes.Length; i++)
            {
                if (chainLink.ResourceTypes[i] != ResourceTypes[i])
                {
                    return false;
                }
            }

            for (var i = 0; i < TargetResourceTypes.Length; i++)
            {
                if (chainLink.TargetResourceTypes[i] != TargetResourceTypes[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
