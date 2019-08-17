// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    public class ApiNotificationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IMediator _mediator;

        public ApiNotificationMiddleware(
            RequestDelegate next,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IMediator mediator)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _next = next;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _mediator = mediator;
        }

        public async Task Invoke(HttpContext context)
        {
            var notification = new ApiResponseNotification();

            try
            {
                await _next(context);
            }
            finally
            {
                notification.SetLatency();
                RouteData routeData = context.GetRouteData();

                object resourceType = null;

                if (routeData != null && routeData.Values != null)
                {
                    routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out resourceType);
                }

                notification.Authentication = _fhirRequestContextAccessor.FhirRequestContext.Principal.Identity.AuthenticationType;
                notification.Operation = _fhirRequestContextAccessor.FhirRequestContext.RouteName;
                notification.Protocol = context.Request.Scheme;
                notification.ResourceType = resourceType?.ToString();
                notification.StatusCode = (HttpStatusCode)context.Response.StatusCode;

                await _mediator.Publish<ApiResponseNotification>(notification, context.RequestAborted);
            }
        }
    }
}
