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
    public class FhirRequestContextMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirRequestContextMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public Task Invoke(HttpContext context, IFhirRequestContextAccessor fhirRequestContextAccessor, CorrelationIdProvider correlationIdProvider)
        {
            HttpRequest request = context.Request;

            var fhirRequestContext = new FhirRequestContext(
                request.Method,
                request.Scheme,
                request.Host.HasValue ? request.Host.Host : string.Empty,
                request.Host.Port,
                request.PathBase.HasValue ? request.PathBase.ToUriComponent() : string.Empty,
                request.Path.HasValue ? request.Path.ToUriComponent() : string.Empty,
                request.QueryString.HasValue ? request.QueryString.ToUriComponent() : string.Empty,
                ValueSets.AuditEventType.RestFulOperation,
                correlationIdProvider.Invoke());

            if (context.User != null)
            {
                fhirRequestContext.Principal = context.User;
            }

            fhirRequestContextAccessor.FhirRequestContext = fhirRequestContext;

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
