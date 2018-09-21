// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
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

            string baseUriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase);

            string uriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase,
                request.Path,
                request.QueryString);

            var fhirRequestContext = new FhirRequestContext(
                request.Method,
                uriInString,
                baseUriInString,
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
