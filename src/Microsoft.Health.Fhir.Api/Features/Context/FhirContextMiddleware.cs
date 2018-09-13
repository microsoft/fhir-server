// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FhirContextMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirContextMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public Task Invoke(HttpContext context, IFhirContextAccessor fhirContextAccessor, CorrelationIdProvider correlationIdProvider)
        {
            fhirContextAccessor.FhirContext = new FhirContext(correlationIdProvider.Invoke())
            {
                RequestType = ValueSets.AuditEventType.RestFulOperation,
                Principal = context.User,
                RequestUri = context.Request.Path.HasValue ? new Uri(context.Request.GetDisplayUrl()) : null,
                HttpMethod = context.Request.Method,
                RequestHeaders = context.Request.Headers,
                ResponseHeaders = context.Response.Headers,
            };

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
