// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// determines access based on the SMART scope.
    /// </summary>
    public static class SmartScopeFhirAuthorizationService
    {
        /// <summary>
        /// Checks SMART permissions for the request's resource type.
        /// </summary>
        /// <param name="requestContextAccessor">The request context accessor.</param>
        /// <param name="dataActions">The requested actions.</param>
        /// <returns>The permitted subset of the requested actions.</returns>
        public static DataActions CheckSMARTScopeAccess(this RequestContextAccessor<IFhirRequestContext> requestContextAccessor, DataActions dataActions)
        {
            return requestContextAccessor.CheckSMARTScopeAccess(dataActions, requestContextAccessor.RequestContext.ResourceType);
        }

        /// <summary>
        /// Checks SMART permissions for an explicit resource type without changing the request context.
        /// </summary>
        /// <param name="requestContextAccessor">The request context accessor.</param>
        /// <param name="dataActions">The requested actions.</param>
        /// <param name="resourceRequested">The resource type to authorize.</param>
        /// <returns>The permitted subset of the requested actions.</returns>
        public static DataActions CheckSMARTScopeAccess(this RequestContextAccessor<IFhirRequestContext> requestContextAccessor, DataActions dataActions, string resourceRequested)
        {
            var allowedResourceActions = requestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions;

            DataActions permittedDataActions = 0;
            foreach (ScopeRestriction scopeRestriction in allowedResourceActions)
            {
                // resourceRequested is null when the base route is queried, i.e. all resources
                // or resourceRequested is * when querying all resources in a compartment
                if (scopeRestriction.Resource == KnownResourceTypes.All ||
                    scopeRestriction.Resource == resourceRequested ||
                    resourceRequested == null ||
                    resourceRequested == "*")
                {
                    permittedDataActions |= scopeRestriction.AllowedDataAction;
                    if (permittedDataActions == dataActions)
                    {
                        break;
                    }
                }
            }

            return dataActions & permittedDataActions;
        }
    }
}
