// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// A <see cref="IAuthorizationService"/> that determines access based on the current principal's role memberships.
    /// </summary>
    internal class RoleBasedFhirAuthorizationService : IAuthorizationService<DataActions>
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly Dictionary<string, Role> _roles;
        private readonly string _rolesClaimName;

        public RoleBasedFhirAuthorizationService(AuthorizationConfiguration authorizationConfiguration, RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            _requestContextAccessor = requestContextAccessor;
            _rolesClaimName = authorizationConfiguration.RolesClaim;
            _roles = authorizationConfiguration.Roles.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        }

        public ValueTask<DataActions> CheckAccess(DataActions dataActions, CancellationToken cancellationToken)
        {
            ClaimsPrincipal principal = _requestContextAccessor.RequestContext.Principal;
            bool applySMARTAccess = _requestContextAccessor.RequestContext.AccessControlContext.ApplyFineGrainedAccessControl;

            DataActions permittedDataActions = 0;
            foreach (Claim claim in principal.FindAll(_rolesClaimName))
            {
                if (_roles.TryGetValue(claim.Value, out Role role))
                {
                    permittedDataActions |= role.AllowedDataActions;
                    if (permittedDataActions == dataActions)
                    {
                        break;
                    }
                }
            }

            if (applySMARTAccess)
            {
                return new ValueTask<DataActions>(dataActions & permittedDataActions & SmartScopeFhirAuthorizationService.CheckSMARTScopeAccess(_requestContextAccessor, dataActions));
            }

            return new ValueTask<DataActions>(dataActions & permittedDataActions);
        }
    }
}
