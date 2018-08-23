// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Security;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class SecurityModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            // You can create your own FhirAccessRequirementHandler by implementating an IAuthorizationHandler.
            // Replace the DefaultFhirAccessRequirementHandler here with your custom implementation to have it handle the FhirAccessRequirement.
            services.AddSingleton<IAuthorizationHandler, DefaultFhirAccessRequirementHandler>();
        }
    }
}
