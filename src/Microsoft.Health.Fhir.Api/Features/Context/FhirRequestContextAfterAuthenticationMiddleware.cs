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
    public class FhirRequestContextAfterAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirRequestContextAfterAuthenticationMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public Task Invoke(HttpContext context, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            // Set the user again to pick up anything that was set during authentication, e.g. claims.
            if (context.User != null)
            {
                fhirRequestContextAccessor.FhirRequestContext.Principal = context.User;
            }

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
