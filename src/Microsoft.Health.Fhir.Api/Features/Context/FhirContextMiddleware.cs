// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
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
            };

            HttpRequest request = context.Request;

            if (!string.IsNullOrEmpty(request?.Path) && !string.IsNullOrEmpty(context.Request.Method))
            {
                fhirContextAccessor.FhirContext.RequestUri = new Uri(context.Request.GetDisplayUrl());
                fhirContextAccessor.FhirContext.HttpMethod = new HttpMethod(context.Request.Method);
            }

            if (request != null &&
                request.Host.HasValue)
            {
                fhirContextAccessor.FhirContext.BaseUri = new Uri(
                    $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}");
            }

            if (context.User != null)
            {
                fhirContextAccessor.FhirContext.Principal = context.User;
            }

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
