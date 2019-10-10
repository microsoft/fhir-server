// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Resources.Bundle
{
    public class BundleHandler : IRequestHandler<BundleRequest, BundleResponse>
    {
        private IHttpContextAccessor _httpContextAccessor;
        private IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private FhirJsonSerializer _fhirJsonSerializer;
        private FhirJsonParser _fhirJsonParser;

        public BundleHandler(IHttpContextAccessor httpContextAccessor, IFhirRequestContextAccessor fhirRequestContextAccessor, FhirJsonSerializer fhirJsonSerializer, FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));

            _httpContextAccessor = httpContextAccessor;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;
        }

        public async Task<BundleResponse> Handle(BundleRequest bundleRequest, CancellationToken cancellationToken)
        {
            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            var requests = new Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>>
            {
                { Hl7.Fhir.Model.Bundle.HTTPVerb.GET, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.POST, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.PUT, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
            };

            var httpAuthenticationFeature = _httpContextAccessor.HttpContext.Features.First(x => x.Key == typeof(IHttpAuthenticationFeature)).Value as IHttpAuthenticationFeature;

            var router = _httpContextAccessor.HttpContext.GetRouteData().Routers.First();

            foreach (var entry in bundleResource.Entry)
            {
                HttpContext context = new DefaultHttpContext { RequestServices = _httpContextAccessor.HttpContext.RequestServices };
                context.Response.Body = new MemoryStream();

                string requestPath = entry.Request.Url;

                if (!requestPath.StartsWith('/'))
                {
                    requestPath = "/" + requestPath;
                }

                var splitPath = requestPath.Split('?');

                if (splitPath.Length > 1)
                {
                    context.Request.QueryString = new QueryString($"?{splitPath[1]}");
                }

                context.Request.Path = splitPath[0];
                context.Request.Method = entry.Request.Method.ToString();

                foreach (var header in _fhirRequestContextAccessor.FhirRequestContext.RequestHeaders)
                {
                    context.Request.Headers.Add(header);
                }

                switch (entry.Request.Method)
                {
                    case Hl7.Fhir.Model.Bundle.HTTPVerb.POST:
                    case Hl7.Fhir.Model.Bundle.HTTPVerb.PUT:
                        byte[] serialized = _fhirJsonSerializer.SerializeToBytes(entry.Resource);
                        var stringSerialized = _fhirJsonSerializer.SerializeToString(entry.Resource);
                        var memoryStream = new MemoryStream();
                        memoryStream.Write(serialized);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        context.Request.Body = memoryStream;
                        break;
                }

                var routeContext = new RouteContext(context);

                await router.RouteAsync(routeContext);

                if (routeContext.Handler == null)
                {
                    continue;
                }

                context.Features[typeof(IRoutingFeature)] = new RoutingFeature()
                {
                    RouteData = routeContext.RouteData,
                };
                context.Features[typeof(IHttpAuthenticationFeature)] = httpAuthenticationFeature;

                requests[entry.Request.Method.Value].Add((requestPath, context, routeContext));
            }

            var responseBundle = new Hl7.Fhir.Model.Bundle();

            await ExecuteRequests(responseBundle, requests[Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE]);
            await ExecuteRequests(responseBundle, requests[Hl7.Fhir.Model.Bundle.HTTPVerb.POST]);
            await ExecuteRequests(responseBundle, requests[Hl7.Fhir.Model.Bundle.HTTPVerb.PUT]);
            await ExecuteRequests(responseBundle, requests[Hl7.Fhir.Model.Bundle.HTTPVerb.GET]);

            return new BundleResponse(responseBundle.ToResourceElement());
        }

        private async Task ExecuteRequests(Hl7.Fhir.Model.Bundle responseBundle, List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)> requests)
        {
            foreach (var request in requests)
            {
                await request.routeContext.Handler.Invoke(request.routeContext.HttpContext);

                request.httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
                string bodyContent = new StreamReader(request.httpContext.Response.Body).ReadToEnd();

                var entryComponent = new Hl7.Fhir.Model.Bundle.EntryComponent();
                if (!string.IsNullOrWhiteSpace(bodyContent))
                {
                    entryComponent.Resource = _fhirJsonParser.Parse<Resource>(bodyContent);
                }

                entryComponent.Response = new Hl7.Fhir.Model.Bundle.ResponseComponent { Status = request.httpContext.Response.StatusCode.ToString() };

                responseBundle.Entry.Add(entryComponent);
            }
        }
    }
}
