// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Access;

public class ExpressionAccessControl
{
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

    public ExpressionAccessControl(RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
    {
        _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
    }

    public void CheckAndRaiseAccessExceptions(Expression expression)
    {
        CheckAndRaiseAccessExceptions(
            expression,
            _requestContextAccessor.RequestContext?.AccessControlContext?.AllowedResourceActions?.ToList());
    }

    public void CheckAndRaiseAccessExceptions(
        Expression expression,
        IReadOnlyCollection<ScopeRestriction> grantedScopes)
    {
        if (expression == null)
        {
            return;
        }

        if (_requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
        {
            var collector = new AccessCheckedExpressionCollector();
            expression.AcceptVisitor(collector, null);

            var validResourceTypes = grantedScopes?.Select(r => r.Resource).ToHashSet();

            // check resource type restrictions from SMART clinical scopes
            foreach (var type in collector.ChainedExpressions
                         .SelectMany(x => x.Reversed ? x.ResourceTypes : x.TargetResourceTypes))
            {
                if (!ResourceTypeAllowedByClinicalScopes(validResourceTypes, type))
                {
                    throw new InvalidSearchOperationException(Core.Resources.ChainedResourceTypesNotAllowedDueToScope);
                }
            }

            IEnumerable<string> typesToCheck =
                collector.IncludeExpressions.SelectMany(x => x.Produces)
                .Concat(collector.RevIncludeExpressions.SelectMany(x => x.ResourceTypes))
                .ToHashSet();

            foreach (var type in typesToCheck)
            {
                if (!ResourceTypeAllowedByClinicalScopes(validResourceTypes, type))
                {
                    throw new InvalidSearchOperationException(string.Format(Core.Resources.ResourceTypeNotAllowedRestrictedByClinicalScopes, type));
                }
            }
        }
    }

    /// <summary>
    /// Walks the whole expression tree so chained/_has and include expressions are checked at any nesting depth.
    /// </summary>
    private sealed class AccessCheckedExpressionCollector : ExpressionRewriter<object>
    {
        public List<ChainedExpression> ChainedExpressions { get; } = new();

        public List<IncludeExpression> IncludeExpressions { get; } = new();

        public List<IncludeExpression> RevIncludeExpressions { get; } = new();

        public override Expression VisitChained(ChainedExpression expression, object context)
        {
            ChainedExpressions.Add(expression);
            return base.VisitChained(expression, context);
        }

        public override Expression VisitInclude(IncludeExpression expression, object context)
        {
            (expression.Reversed ? RevIncludeExpressions : IncludeExpressions).Add(expression);
            return expression;
        }

        // Scope-injected predicates come from the token's own scope definition, not the caller's query,
        // so they are not subject to the caller-facing checks (matches the previous behavior).
        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            return expression.IsSmartV2UnionExpressionForScopesSearchParameters ? expression : base.VisitMultiary(expression, context);
        }

        public override Expression VisitUnion(UnionExpression expression, object context)
        {
            return expression.IsSmartV2UnionExpressionForScopesSearchParameters ? expression : base.VisitUnion(expression, context);
        }
    }

    private static bool ResourceTypeAllowedByClinicalScopes(HashSet<string> validResourceTypes, string resourceType)
    {
        if (validResourceTypes != null && (validResourceTypes.Contains(KnownResourceTypes.All) || validResourceTypes.Contains(resourceType)))
        {
            return true;
        }

        return false;
    }
}
