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
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateActionPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    {
        private readonly IAuthorizationPolicy _roleBasedAuthorizationPolicy;
        private readonly IFhirRequestContextAccessor _fhirRquestContextAccessor;
        private readonly SecurityConfiguration _securityConfiguration;

        public ValidateActionPreProcessor(IAuthorizationPolicy roleBasedAuthorizationPolicy, IFhirRequestContextAccessor fhirRquestContextAccessor, IOptions<SecurityConfiguration> securityConfiguration)
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
                _fhirRquestContextAccessor.FhirRequestContext.ApplicableResourcePermissions = _roleBasedAuthorizationPolicy.GetApplicableResourcePermissions(_fhirRquestContextAccessor.FhirRequestContext.Principal, provider.RequiredAction());
            }

            return Task.CompletedTask;
        }
    }
}
