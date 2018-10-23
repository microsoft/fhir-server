// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateActionPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    {
        private readonly IAuthorizationPolicy _authorizationPolicyClient;
        private readonly IFhirRequestContextAccessor _fhirRquestContextAccessor;

        public ValidateActionPreProcessor(IAuthorizationPolicy authorizationPolicyClient, IFhirRequestContextAccessor fhirRquestContextAccessor)
        {
            EnsureArg.IsNotNull(authorizationPolicyClient, nameof(authorizationPolicyClient));
            EnsureArg.IsNotNull(fhirRquestContextAccessor, nameof(fhirRquestContextAccessor));

            _authorizationPolicyClient = authorizationPolicyClient;
            _fhirRquestContextAccessor = fhirRquestContextAccessor;
        }

        public Task Process(TRequest request, CancellationToken cancellationToken)
        {
            if (request is IRequireAction provider)
            {
                foreach (var action in provider.RequiredActions())
                {
                    var applicablePermission = _authorizationPolicyClient.GetApplicableResourcePermissions(_fhirRquestContextAccessor.FhirRequestContext.Principal, action);

                    if (applicablePermission == null || !applicablePermission.Any())
                    {
                        throw new Exception("Forbidden");
                    }

                    _fhirRquestContextAccessor.FhirRequestContext.ApplicableResourcePermissions = _fhirRquestContextAccessor.FhirRequestContext.ApplicableResourcePermissions.Concat(applicablePermission);
                }
            }

            return Task.CompletedTask;
        }
    }
}
