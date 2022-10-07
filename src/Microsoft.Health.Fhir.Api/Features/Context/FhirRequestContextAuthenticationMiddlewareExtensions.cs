// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Features.Smart;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public static class FhirRequestContextAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseFhirRequestContextAuthentication(
            this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.UseMiddleware<FhirRequestContextBeforeAuthenticationMiddleware>();

            builder.UseAuthentication();

            builder.UseMiddleware<FhirRequestContextAfterAuthenticationMiddleware>();

            builder.UseMiddleware<SmartClinicalScopesMiddleware>();

            return builder;
        }
    }
}
