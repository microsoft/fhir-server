// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class RequestAuthorizationPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    {
        private readonly IAuthorizationPolicy _roleBasedAuthorizationPolicy;
        private readonly IFhirRequestContextAccessor _fhirRquestContextAccessor;
        private readonly SecurityConfiguration _securityConfiguration;

        public RequestAuthorizationPreProcessor(IAuthorizationPolicy roleBasedAuthorizationPolicy, IFhirRequestContextAccessor fhirRquestContextAccessor, IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(roleBasedAuthorizationPolicy, nameof(roleBasedAuthorizationPolicy));
            EnsureArg.IsNotNull(fhirRquestContextAccessor, nameof(fhirRquestContextAccessor));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _roleBasedAuthorizationPolicy = roleBasedAuthorizationPolicy;
            _fhirRquestContextAccessor = fhirRquestContextAccessor;
            _securityConfiguration = securityConfiguration.Value;
        }

        public Task Process(TRequest request, CancellationToken cancellationToken)
        {
            if (request is IRequireAction provider && _securityConfiguration.Authorization.Enabled)
            {
                // Get the applicable resource permissions
                var applicableResourcePermissions = _roleBasedAuthorizationPolicy.GetApplicableResourcePermissions(_fhirRquestContextAccessor.FhirRequestContext.Principal, provider.RequiredAction);

                // Using the applicable resource permissions, we will evaluate them to retrieve an Expression, that will then be added to the FhirRequestContext
            }

            return Task.CompletedTask;
        }
    }
}
