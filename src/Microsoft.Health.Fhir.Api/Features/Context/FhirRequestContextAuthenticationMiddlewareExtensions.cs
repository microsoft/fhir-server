// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Features.Smart;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public static class FhirRequestContextAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseFhirRequestContextAuthentication(
            this IApplicationBuilder builder,
            Action<IApplicationBuilder> configureAfterAuthentication = null)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.UseMiddleware<FhirRequestContextBeforeAuthenticationMiddleware>();

            builder.UseMiddleware<FhirAuthenticationExceptionHandlerMiddleware>();

            builder.UseAuthentication();

            builder.UseMiddleware<FhirRequestContextAfterAuthenticationMiddleware>();

            configureAfterAuthentication?.Invoke(builder);

            builder.UseMiddleware<SmartClinicalScopesMiddleware>();

            return builder;
        }
    }
}
