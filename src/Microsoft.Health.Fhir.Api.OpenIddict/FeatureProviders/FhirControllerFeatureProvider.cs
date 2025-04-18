// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Api.OpenIddict.Controllers;

namespace Microsoft.Health.Fhir.Api.OpenIddict.FeatureProviders
{
    public sealed class FhirControllerFeatureProvider : ControllerFeatureProvider
    {
        private const string DevelopmentIdpEnabledKey = "DevelopmentIdentityProvider:Enabled";

        private readonly bool _developmentIdentityProviderEnabled;

        public FhirControllerFeatureProvider(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _developmentIdentityProviderEnabled = bool.TryParse(configuration[DevelopmentIdpEnabledKey], out var value) && value;
        }

        protected override bool IsController(TypeInfo typeInfo)
        {
            if (typeInfo.AsType() == typeof(OpenIddictAuthorizationController))
            {
                return _developmentIdentityProviderEnabled;
            }

            return base.IsController(typeInfo);
        }
    }
}
