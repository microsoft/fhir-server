// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    /// <summary>
    /// Middleware that runs after authentication middleware so that it can retrieved authenticated user claims.
    /// </summary>
    public class FhirRequestContextAfterAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirRequestContextAfterAuthenticationMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public async Task Invoke(HttpContext context, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            // Now the authentication is completed successfully, sets the user.
            if (context.User != null)
            {
                fhirRequestContextAccessor.FhirRequestContext.Principal = context.User;
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
