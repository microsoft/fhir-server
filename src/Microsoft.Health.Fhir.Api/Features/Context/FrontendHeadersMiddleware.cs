// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FrontendHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public FrontendHeadersMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            context.Request.Scheme = "https";

            await _next(context);
        }
    }
}
