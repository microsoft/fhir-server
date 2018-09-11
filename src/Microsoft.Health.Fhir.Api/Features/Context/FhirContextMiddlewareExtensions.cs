// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public static class FhirContextMiddlewareExtensions
    {
        public static IApplicationBuilder UseFhirContext(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FhirContextMiddleware>();
        }
    }
}
