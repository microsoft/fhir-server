// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>> _requests;
        private readonly IHttpAuthenticationFeature _httpAuthenticationFeature;
        private readonly IRouter _router;
        private readonly IServiceProvider _requestServices;

        public BundleHandler(IHttpContextAccessor httpContextAccessor, IFhirRequestContextAccessor fhirRequestContextAccessor, FhirJsonSerializer fhirJsonSerializer, FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;

            _requests = new Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>>
            {
                { Hl7.Fhir.Model.Bundle.HTTPVerb.GET, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.POST, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.PUT, new List<(string originalUrl, HttpContext httpContext, RouteContext routeContext)>() },
            };

            _httpAuthenticationFeature = httpContextAccessor.HttpContext.Features.First(x => x.Key == typeof(IHttpAuthenticationFeature)).Value as IHttpAuthenticationFeature;
            _router = httpContextAccessor.HttpContext.GetRouteData().Routers.First();
            _requestServices = httpContextAccessor.HttpContext.RequestServices;
        }

        public async Task<BundleResponse> Handle(BundleRequest bundleRequest, CancellationToken cancellationToken)
        {
            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            if (bundleResource.Type == Hl7.Fhir.Model.Bundle.BundleType.Transaction)
            {
                throw new System.NotImplementedException();
            }

            await FillRequestLists(bundleResource.Entry);

            var responseBundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.BatchResponse,
            };

            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.POST);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.PUT);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.GET);

            return new BundleResponse(responseBundle.ToResourceElement());
        }

        private async Task FillRequestLists(List<Hl7.Fhir.Model.Bundle.EntryComponent> bundleEntries)
        {
            foreach (Hl7.Fhir.Model.Bundle.EntryComponent entry in bundleEntries)
            {
                HttpContext httpContext = new DefaultHttpContext { RequestServices = _requestServices };
                httpContext.Features[typeof(IHttpAuthenticationFeature)] = _httpAuthenticationFeature;
                httpContext.Response.Body = new MemoryStream();

                string requestPath = entry.Request.Url;

                if (!requestPath.StartsWith('/'))
                {
                    requestPath = "/" + requestPath;
                }

                var splitPath = requestPath.Split('?');

                if (splitPath.Length > 1)
                {
                    httpContext.Request.QueryString = new QueryString($"?{splitPath[1]}");
                }

                httpContext.Request.Path = splitPath[0];
                httpContext.Request.Method = entry.Request.Method.ToString();

                foreach (var header in _fhirRequestContextAccessor.FhirRequestContext.RequestHeaders)
                {
                    httpContext.Request.Headers.Add(header);
                }

                switch (entry.Request.Method)
                {
                    case Hl7.Fhir.Model.Bundle.HTTPVerb.POST:
                    case Hl7.Fhir.Model.Bundle.HTTPVerb.PUT:
                        var memoryStream = new MemoryStream(_fhirJsonSerializer.SerializeToBytes(entry.Resource));
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        httpContext.Request.Body = memoryStream;
                        break;
                }

                var routeContext = new RouteContext(httpContext);

                await _router.RouteAsync(routeContext);

                if (routeContext.Handler == null)
                {
                    continue;
                }

                httpContext.Features[typeof(IRoutingFeature)] = new RoutingFeature()
                {
                    RouteData = routeContext.RouteData,
                };

                _requests[entry.Request.Method.Value].Add((requestPath, httpContext, routeContext));
            }
        }

        private async Task ExecuteRequests(Hl7.Fhir.Model.Bundle responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb httpVerb)
        {
            foreach (var request in _requests[httpVerb])
            {
                await request.routeContext.Handler.Invoke(request.routeContext.HttpContext);

                request.httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
                string bodyContent = new StreamReader(request.httpContext.Response.Body).ReadToEnd();

                var entryComponent = new Hl7.Fhir.Model.Bundle.EntryComponent
                {
                    Response = new Hl7.Fhir.Model.Bundle.ResponseComponent
                    {
                        Status = request.httpContext.Response.StatusCode.ToString(),
                        Location = request.httpContext.Response.Headers["Location"],
                        Etag = request.httpContext.Response.Headers["ETag"],
                    },
                };

                string lastModifiedHeader = request.httpContext.Response.Headers["Last-Modified"];
                if (!string.IsNullOrWhiteSpace(lastModifiedHeader))
                {
                    entryComponent.Response.LastModified = DateTimeOffset.Parse(lastModifiedHeader);
                }

                if (!string.IsNullOrWhiteSpace(bodyContent))
                {
                    var entryComponentResource = _fhirJsonParser.Parse<Resource>(bodyContent);

                    if (entryComponentResource.ResourceType == ResourceType.OperationOutcome)
                    {
                        entryComponent.Response.Outcome = entryComponentResource;
                    }
                    else
                    {
                        entryComponent.Resource = entryComponentResource;
                    }
                }

                responseBundle.Entry.Add(entryComponent);
            }
        }
    }
}
