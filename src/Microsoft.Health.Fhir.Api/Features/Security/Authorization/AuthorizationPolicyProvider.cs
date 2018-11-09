// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Api.Features.Security.Authorization
{
    public class AuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        private readonly AuthorizationOptions _options;

        public AuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
            : base(options)
        {
            _options = options.Value;
        }

        public override async Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(policyName, nameof(policyName));

            AuthorizationPolicy policy = await base.GetPolicyAsync(policyName);

            if (policy == null)
            {
                policy = new AuthorizationPolicyBuilder().AddRequirements(new ResourceActionRequirement(policyName)).Build();

                // This caches the policy in the base class
                _options.AddPolicy(policyName, policy);
            }

            return policy;
        }
    }
}
