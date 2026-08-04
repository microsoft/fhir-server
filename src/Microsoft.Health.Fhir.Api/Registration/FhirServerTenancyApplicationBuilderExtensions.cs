// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Features.Tenancy;

namespace Microsoft.Health.Fhir.Api.Registration
{
    /// <summary>
    /// Adds tenant-specific middleware to the FHIR request pipeline.
    /// </summary>
    public static class FhirServerTenancyApplicationBuilderExtensions
    {
        /// <summary>
        /// Inserts <see cref="TenantMiddleware"/> into the request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The same application builder for chaining.</returns>
        public static IApplicationBuilder UseFhirTenancy(this IApplicationBuilder app)
        {
            EnsureArg.IsNotNull(app, nameof(app));

            return app.UseMiddleware<TenantMiddleware>();
        }
    }
}
