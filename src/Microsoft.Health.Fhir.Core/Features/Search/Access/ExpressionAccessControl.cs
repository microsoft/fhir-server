// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
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
        if (expression == null)
        {
            return;
        }

        if (_requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
        {
            if (expression.ExtractIncludeAndChainedExpressions(
                    out _,
                    out IReadOnlyList<IncludeExpression> includeExpressions,
                    out IReadOnlyList<IncludeExpression> revIncludeExpressions,
                    out IReadOnlyList<ChainedExpression> chainedExpressions))
            {
                var validResourceTypes = _requestContextAccessor.RequestContext?.AccessControlContext.AllowedResourceActions.Select(r => r.Resource).ToHashSet();

                // check resource type restrictions from SMART clinical scopes
                foreach (var type in chainedExpressions
                             .SelectMany(x => x.Reversed ? x.ResourceTypes : x.TargetResourceTypes))
                {
                    if (!ResourceTypeAllowedByClinicalScopes(validResourceTypes, type))
                    {
                        throw new InvalidSearchOperationException(Core.Resources.ChainedResourceTypesNotAllowedDueToScope);
                    }
                }

                IEnumerable<string> typesToCheck =
                    includeExpressions.SelectMany(x => x.Produces)
                    .Concat(revIncludeExpressions.SelectMany(x => x.ResourceTypes))
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
