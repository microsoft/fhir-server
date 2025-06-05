// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    public class ExportTestProviderMiddleware
    {
        private readonly RequestDelegate _next;

        public ExportTestProviderMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Call the next middleware in the pipeline
            await _next(context);

            await context.Response.StartAsync();
        }
    }
}
