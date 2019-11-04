// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Handler for bundles of type transaction and batch.
    /// </summary>
    public partial class BundleHandler : IRequestHandler<BundleRequest, BundleResponse>
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<RouteContext>> _requests;
        private readonly IHttpAuthenticationFeature _httpAuthenticationFeature;
        private readonly IRouter _router;
        private readonly IServiceProvider _requestServices;
        private readonly BundleHttpContextAccessor _bundleHttpContextAccessor;

        public BundleHandler(IHttpContextAccessor httpContextAccessor, IFhirRequestContextAccessor fhirRequestContextAccessor, FhirJsonSerializer fhirJsonSerializer, FhirJsonParser fhirJsonParser, BundleHttpContextAccessor bundleHttpContextAccessor)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;
            _bundleHttpContextAccessor = bundleHttpContextAccessor;

            // Not all versions support the same enum values, so do the dictionary creation in the version specific partial.
            _requests = GenerateRequestDictionary();

            _httpAuthenticationFeature = httpContextAccessor.HttpContext.Features.First(x => x.Key == typeof(IHttpAuthenticationFeature)).Value as IHttpAuthenticationFeature;
            _router = httpContextAccessor.HttpContext.GetRouteData().Routers.First();
            _requestServices = httpContextAccessor.HttpContext.RequestServices;
        }

        public async Task<BundleResponse> Handle(BundleRequest bundleRequest, CancellationToken cancellationToken)
        {
            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            if (bundleResource.Type != Hl7.Fhir.Model.Bundle.BundleType.Batch)
            {
                throw new MethodNotAllowedException(Microsoft.Health.Fhir.Api.Resources.OnlyCertainBundleTypesSupported);
            }

            await FillRequestLists(bundleResource.Entry);

            var responseBundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.BatchResponse,
            };

            await ExecuteAllRequests(responseBundle);

            return new BundleResponse(responseBundle.ToResourceElement());
        }

        private async Task FillRequestLists(List<Hl7.Fhir.Model.Bundle.EntryComponent> bundleEntries)
        {
            foreach (Hl7.Fhir.Model.Bundle.EntryComponent entry in bundleEntries)
            {
                if (entry.Request.Method == null)
                {
                    continue;
                }

                HttpContext httpContext = new DefaultHttpContext { RequestServices = _requestServices };
                httpContext.Features[typeof(IHttpAuthenticationFeature)] = _httpAuthenticationFeature;
                httpContext.Response.Body = new MemoryStream();

                var requestUri = new Uri(_fhirRequestContextAccessor.FhirRequestContext.BaseUri, entry.Request.Url);
                httpContext.Request.Path = requestUri.LocalPath;
                httpContext.Request.QueryString = new QueryString(requestUri.Query);
                httpContext.Request.Method = entry.Request.Method.ToString();

                AddHeaderIfNeeded(HeaderNames.IfMatch, entry.Request.IfMatch, httpContext);
                AddHeaderIfNeeded(HeaderNames.IfModifiedSince, entry.Request.IfModifiedSince?.ToString(), httpContext);
                AddHeaderIfNeeded(HeaderNames.IfNoneMatch, entry.Request.IfNoneMatch, httpContext);
                AddHeaderIfNeeded(KnownFhirHeaders.IfNoneExist, entry.Request.IfNoneExist, httpContext);

                if (entry.Request.Method == Hl7.Fhir.Model.Bundle.HTTPVerb.POST ||
                   entry.Request.Method == Hl7.Fhir.Model.Bundle.HTTPVerb.PUT)
                {
                    httpContext.Request.Headers.Add(HeaderNames.ContentType, new StringValues(KnownContentTypes.JsonContentType));

                    var memoryStream = new MemoryStream(_fhirJsonSerializer.SerializeToBytes(entry.Resource));
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    httpContext.Request.Body = memoryStream;
                }

                var routeContext = new RouteContext(httpContext);

                await _router.RouteAsync(routeContext);

                httpContext.Features[typeof(IRoutingFeature)] = new RoutingFeature()
                {
                    RouteData = routeContext.RouteData,
                };

                _requests[entry.Request.Method.Value].Add(routeContext);
            }
        }

        private static void AddHeaderIfNeeded(string headerKey, string headerValue, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                httpContext.Request.Headers.Add(headerKey, new StringValues(headerValue));
            }
        }

        private async Task ExecuteRequests(Hl7.Fhir.Model.Bundle responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb httpVerb)
        {
            foreach (RouteContext request in _requests[httpVerb])
            {
                var entryComponent = new Hl7.Fhir.Model.Bundle.EntryComponent();

                if (request.Handler != null)
                {
                    var httpContext = request.HttpContext;

                    _bundleHttpContextAccessor.HttpContext = httpContext;

                    await request.Handler.Invoke(httpContext);

                    httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
                    string bodyContent = new StreamReader(httpContext.Response.Body).ReadToEnd();

                    ResponseHeaders responseHeaders = httpContext.Response.GetTypedHeaders();
                    entryComponent.Response = new Hl7.Fhir.Model.Bundle.ResponseComponent
                    {
                        Status = httpContext.Response.StatusCode.ToString(),
                        Location = responseHeaders.Location?.OriginalString,
                        Etag = responseHeaders.ETag?.ToString(),
                        LastModified = responseHeaders.LastModified,
                    };

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
                }
                else
                {
                    entryComponent.Response = new Hl7.Fhir.Model.Bundle.ResponseComponent
                    {
                        Status = ((int)HttpStatusCode.NotFound).ToString(),
                        Outcome = new OperationOutcome
                        {
                            Id = Guid.NewGuid().ToString(),
                            Issue = new List<OperationOutcome.IssueComponent>
                            {
                                new OperationOutcome.IssueComponent
                                {
                                    Severity = OperationOutcome.IssueSeverity.Error,
                                    Code = OperationOutcome.IssueType.NotFound,
                                    Diagnostics = string.Format(Api.Resources.BundleNotFound, $"{request.HttpContext.Request.Path}{request.HttpContext.Request.QueryString}"),
                                },
                            },
                        },
                    };
                }

                responseBundle.Entry.Add(entryComponent);
            }
        }
    }
}
