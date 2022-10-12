// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// determines access based on the SMART scope.
    /// </summary>
    public static class SMARTScopeFhirAuthorizationService
    {
        public static DataActions CheckSMARTScopeAccess(RequestContextAccessor<IFhirRequestContext> requestContextAccessor, DataActions dataActions)
        {
            var allowedResourceActions = requestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions;
            var resourceRequested = requestContextAccessor.RequestContext.ResourceType;

            DataActions permittedDataActions = 0;
            foreach (ScopeRestriction scopeRestriction in allowedResourceActions)
            {
                if (scopeRestriction.Resource == resourceRequested)
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
