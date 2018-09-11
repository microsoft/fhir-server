// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public class BaseHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public BaseHeadersMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public Task Invoke(HttpContext context)
        {
            context.Response.OnStarting(SecurityHeadersHelper.SetSecurityHeaders, state: context);

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
