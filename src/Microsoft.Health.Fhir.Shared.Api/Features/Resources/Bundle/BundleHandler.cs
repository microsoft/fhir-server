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
using System.Transactions;
using AngleSharp.Io;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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
        private readonly ITransactionHandler _transactionHandler;
        private readonly ILogger<BundleHandler> _logger;

        public BundleHandler(IHttpContextAccessor httpContextAccessor, IFhirRequestContextAccessor fhirRequestContextAccessor, FhirJsonSerializer fhirJsonSerializer, FhirJsonParser fhirJsonParser, ITransactionHandler transaction, ILogger<BundleHandler> logger)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(transaction, nameof(transaction));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;
            _transactionHandler = transaction;
            _logger = logger;

            // Not all versions support the same enum values, so do the dictionary creation in the version specific partial.
            _requests = GenerateRequestDictionary();

            _httpAuthenticationFeature = httpContextAccessor.HttpContext.Features.First(x => x.Key == typeof(IHttpAuthenticationFeature)).Value as IHttpAuthenticationFeature;
            _router = httpContextAccessor.HttpContext.GetRouteData().Routers.First();
            _requestServices = httpContextAccessor.HttpContext.RequestServices;
        }

        public async Task<BundleResponse> Handle(BundleRequest bundleRequest, CancellationToken cancellationToken)
        {
            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            await FillRequestLists(bundleResource.Entry);

            if (bundleResource.Type == Hl7.Fhir.Model.Bundle.BundleType.Batch)
            {
                var responseBundle = new Hl7.Fhir.Model.Bundle
                {
                    Type = Hl7.Fhir.Model.Bundle.BundleType.BatchResponse,
                };

                await ExecuteAllRequests(responseBundle);
                return new BundleResponse(responseBundle.ToResourceElement());
            }

            if (bundleResource.Type == Hl7.Fhir.Model.Bundle.BundleType.Transaction)
            {
                var responseBundle = new Hl7.Fhir.Model.Bundle
                {
                    Type = Hl7.Fhir.Model.Bundle.BundleType.TransactionResponse,
                };

                return await ExecuteTransactionForAllRequests(responseBundle);
            }

            throw new MethodNotAllowedException(Microsoft.Health.Fhir.Api.Resources.UnsupportedOperation);
        }

        private async Task<BundleResponse> ExecuteTransactionForAllRequests(Hl7.Fhir.Model.Bundle responseBundle)
        {
            BundleProcessingStatus bundleProcessingStatus = BundleProcessingStatus.SUCCEEDED;

            try
            {
                try
                {
                    _transactionHandler.BeginTransactionScope();

                    await ExecuteAllRequests(responseBundle);

                    _transactionHandler.CompleteTransactionScope();
                }
                finally
                {
                    _transactionHandler.Dispose();
                }
            }
            catch (TransactionAbortedException ex)
            {
                bundleProcessingStatus = BundleProcessingStatus.FAILED;
                _logger.LogError(ex, "Transaction processing of Bundle is rolled back ");
            }

            return new BundleResponse(responseBundle.ToResourceElement(), bundleProcessingStatus);
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

                // PATCH does not resolve a handler so it current no-ops. This needs to be updated to always return a 404.
                if (routeContext.Handler == null)
                {
                    continue;
                }

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
                HttpContext httpContext = request.HttpContext;
                await request.Handler.Invoke(httpContext);

                httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
                string bodyContent = new StreamReader(httpContext.Response.Body).ReadToEnd();

                var entryComponent = new Hl7.Fhir.Model.Bundle.EntryComponent
                {
                    Response = new Hl7.Fhir.Model.Bundle.ResponseComponent
                    {
                        Status = httpContext.Response.StatusCode.ToString(),
                        Location = httpContext.Response.Headers["Location"],
                        Etag = httpContext.Response.Headers["ETag"],
                    },
                };

                string lastModifiedHeader = httpContext.Response.Headers["Last-Modified"];
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

                        if (responseBundle.Type == Hl7.Fhir.Model.Bundle.BundleType.TransactionResponse)
                        {
                            // Bundle Transaction Response only contains operationoutcome in case of failure.
                            responseBundle.Entry.Clear();
                            responseBundle.Entry.Add(entryComponent);
                            return;
                        }
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
