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
using System.Transactions;
using AngleSharp.Io;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using static Hl7.Fhir.Model.Bundle;
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
        private readonly Dictionary<HTTPVerb, List<(RouteContext, int, string)>> _requests;
        private readonly IHttpAuthenticationFeature _httpAuthenticationFeature;
        private readonly IRouter _router;
        private readonly IServiceProvider _requestServices;
        private readonly ITransactionHandler _transactionHandler;
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly ILogger<BundleHandler> _logger;
        private int _requestCount;
        private readonly HTTPVerb[] _verbExecutionOrder;
        private readonly List<int> _emptyRequestsOrder;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;
        private BundleType? _bundleType;
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly string _originalRequestBase;

        public BundleHandler(
            IHttpContextAccessor httpContextAccessor,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            FhirJsonSerializer fhirJsonSerializer,
            FhirJsonParser fhirJsonParser,
            ITransactionHandler transactionHandler,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            ResourceIdProvider resourceIdProvider,
            TransactionBundleValidator transactionBundleValidator,
            ResourceReferenceResolver referenceResolver,
            IAuditEventTypeMapping auditEventTypeMapping,
            IOptions<BundleConfiguration> bundleConfiguration,
            IFhirAuthorizationService authorizationService,
            ILogger<BundleHandler> logger)
            : this()
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));
            EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            EnsureArg.IsNotNull(transactionBundleValidator, nameof(transactionBundleValidator));
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            EnsureArg.IsNotNull(bundleConfiguration?.Value, nameof(bundleConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;
            _transactionHandler = transactionHandler;
            _bundleHttpContextAccessor = bundleHttpContextAccessor;
            _resourceIdProvider = resourceIdProvider;
            _transactionBundleValidator = transactionBundleValidator;
            _referenceResolver = referenceResolver;
            _auditEventTypeMapping = auditEventTypeMapping;
            _authorizationService = authorizationService;
            _bundleConfiguration = bundleConfiguration.Value;
            _logger = logger;

            // Not all versions support the same enum values, so do the dictionary creation in the version specific partial.
            _requests = _verbExecutionOrder.ToDictionary(verb => verb, _ => new List<(RouteContext, int, string)>());

            _httpAuthenticationFeature = httpContextAccessor.HttpContext.Features.Get<IHttpAuthenticationFeature>();
            _router = httpContextAccessor.HttpContext.GetRouteData().Routers.First();
            _requestServices = httpContextAccessor.HttpContext.RequestServices;
            _originalRequestBase = httpContextAccessor.HttpContext.Request.PathBase;
            _emptyRequestsOrder = new List<int>();
            _referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        private async Task ExecuteAllRequests(Hl7.Fhir.Model.Bundle responseBundle)
        {
            // List is not created initially since it doesn't create a list with _requestCount elements
            responseBundle.Entry = new List<EntryComponent>(new EntryComponent[_requestCount]);
            foreach (int emptyRequestOrder in _emptyRequestsOrder)
            {
                var entryComponent = new EntryComponent
                {
                    Response = new ResponseComponent
                    {
                        Status = ((int)HttpStatusCode.BadRequest).ToString(),
                        Outcome = CreateOperationOutcome(
                            OperationOutcome.IssueSeverity.Error,
                            OperationOutcome.IssueType.Invalid,
                            "Request is empty"),
                    },
                };
                responseBundle.Entry[emptyRequestOrder] = entryComponent;
            }

            foreach (HTTPVerb verb in _verbExecutionOrder)
            {
                await ExecuteRequests(responseBundle, verb);
            }
        }

        public async Task<BundleResponse> Handle(BundleRequest bundleRequest, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(bundleRequest, nameof(bundleRequest));

            // In scenarios where access checks involve a remote service call, it is advantageous
            // to perform one single access check for all necessary permissions rather than one per operation.
            // Two potential TODOs:
            // (1) it would also be better to know what the operations are in the bundle as opposed to checking
            //      for all possible actions. Trouble is, the exact mapping from method + URI is embedded in MVC logic
            //      and attributes.
            // (2) One we have the full set of permitted actions, it would be more efficient for the individual
            //     operations to use an IFhirAuthorizationService that implements CheckAccess based on these known permitted
            //     actions.

            if (await _authorizationService.CheckAccess(DataActions.All) == DataActions.None)
            {
                throw new UnauthorizedFhirActionException();
            }

            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            _bundleType = bundleResource.Type;

            if (_bundleType == BundleType.Batch)
            {
                await FillRequestLists(bundleResource.Entry, cancellationToken);

                var responseBundle = new Hl7.Fhir.Model.Bundle
                {
                    Type = BundleType.BatchResponse,
                };

                await ExecuteAllRequests(responseBundle);
                return new BundleResponse(responseBundle.ToResourceElement());
            }

            if (_bundleType == BundleType.Transaction)
            {
                // For resources within a transaction, we need to validate if they are referring to each other and throw an exception in such case.
                await _transactionBundleValidator.ValidateBundle(bundleResource, _referenceIdDictionary, cancellationToken);

                await FillRequestLists(bundleResource.Entry, cancellationToken);

                var responseBundle = new Hl7.Fhir.Model.Bundle
                {
                    Type = BundleType.TransactionResponse,
                };

                return await ExecuteTransactionForAllRequests(responseBundle);
            }

            throw new MethodNotAllowedException(string.Format(Api.Resources.InvalidBundleType, _bundleType));
        }

        private async Task<BundleResponse> ExecuteTransactionForAllRequests(Hl7.Fhir.Model.Bundle responseBundle)
        {
            try
            {
                using (var transaction = _transactionHandler.BeginTransaction())
                {
                    await ExecuteAllRequests(responseBundle);

                    transaction.Complete();
                }
            }
            catch (TransactionAbortedException)
            {
                _logger.LogError("Failed to commit a transaction. Throwing BadRequest as a default exception.");
                throw new FhirTransactionFailedException(Api.Resources.GeneralTransactionFailedError, HttpStatusCode.BadRequest);
            }

            return new BundleResponse(responseBundle.ToResourceElement());
        }

        private async Task FillRequestLists(List<EntryComponent> bundleEntries, CancellationToken cancellationToken)
        {
            if (_bundleConfiguration.EntryLimit != default && bundleEntries.Count > _bundleConfiguration.EntryLimit)
            {
                throw new BundleEntryLimitExceededException(string.Format(Api.Resources.BundleEntryLimitExceeded, _bundleConfiguration.EntryLimit));
            }

            int order = 0;
            _requestCount = bundleEntries.Count;

            // For a transaction, we need to resolve any references between resources.
            // Loop through the entries and if we're POSTing with an ID in the fullUrl or doing a conditional create then set an ID for it and add it to our dictionary.
            if (_bundleType == BundleType.Transaction)
            {
                PopulateReferenceIdDictionary(bundleEntries, _referenceIdDictionary);
            }

            foreach (EntryComponent entry in bundleEntries)
            {
                if (entry.Request?.Method == null)
                {
                    _emptyRequestsOrder.Add(order++);
                    continue;
                }

                await GenerateRequest(entry, order++, cancellationToken);
            }
        }

        private async Task GenerateRequest(EntryComponent entry, int order, CancellationToken cancellationToken)
        {
            string persistedId = default;
            HttpContext httpContext = new DefaultHttpContext { RequestServices = _requestServices };

            var requestUrl = entry.Request?.Url;

            // For resources within a transaction, we need to resolve any intra-bundle references and potentially persist any internally assigned ids
            HTTPVerb requestMethod = entry.Request.Method.Value;

            if (_bundleType == BundleType.Transaction && entry.Resource != null)
            {
                await _referenceResolver.ResolveReferencesAsync(entry.Resource, _referenceIdDictionary, requestUrl, cancellationToken);

                if (requestMethod == HTTPVerb.POST && !string.IsNullOrWhiteSpace(entry.FullUrl))
                {
                    if (_referenceIdDictionary.TryGetValue(entry.FullUrl, out (string resourceId, string resourceType) value))
                    {
                        persistedId = value.resourceId;
                    }
                }
            }

            httpContext.Features[typeof(IHttpAuthenticationFeature)] = _httpAuthenticationFeature;
            httpContext.Response.Body = new MemoryStream();

            var requestUri = new Uri(_fhirRequestContextAccessor.FhirRequestContext.BaseUri, requestUrl);
            httpContext.Request.Scheme = requestUri.Scheme;
            httpContext.Request.Host = new HostString(requestUri.Host, requestUri.Port);
            httpContext.Request.PathBase = _originalRequestBase;
            httpContext.Request.Path = requestUri.LocalPath;
            httpContext.Request.QueryString = new QueryString(requestUri.Query);
            httpContext.Request.Method = requestMethod.ToString();

            AddHeaderIfNeeded(HeaderNames.IfMatch, entry.Request.IfMatch, httpContext);
            AddHeaderIfNeeded(HeaderNames.IfModifiedSince, entry.Request.IfModifiedSince?.ToString(), httpContext);
            AddHeaderIfNeeded(HeaderNames.IfNoneMatch, entry.Request.IfNoneMatch, httpContext);
            AddHeaderIfNeeded(KnownFhirHeaders.IfNoneExist, entry.Request.IfNoneExist, httpContext);

            if (requestMethod == HTTPVerb.POST ||
                requestMethod == HTTPVerb.PUT)
            {
                httpContext.Request.Headers.Add(HeaderNames.ContentType, new StringValues(KnownContentTypes.JsonContentType));

                var memoryStream = new MemoryStream(_fhirJsonSerializer.SerializeToBytes(entry.Resource));
                memoryStream.Seek(0, SeekOrigin.Begin);
                httpContext.Request.Body = memoryStream;
            }

            var routeContext = new RouteContext(httpContext);

            await _router.RouteAsync(routeContext);

            httpContext.Features[typeof(IRoutingFeature)] = new RoutingFeature
            {
                RouteData = routeContext.RouteData,
            };

            _requests[requestMethod].Add((routeContext, order, persistedId));
        }

        private static void AddHeaderIfNeeded(string headerKey, string headerValue, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                httpContext.Request.Headers.Add(headerKey, new StringValues(headerValue));
            }
        }

        private async Task ExecuteRequests(Hl7.Fhir.Model.Bundle responseBundle, HTTPVerb httpVerb)
        {
            foreach ((RouteContext request, int entryIndex, string persistedId) in _requests[httpVerb])
            {
                EntryComponent entryComponent;

                if (request.Handler != null)
                {
                    HttpContext httpContext = request.HttpContext;

                    SetupContexts(request, httpContext);

                    Func<string> originalResourceIdProvider = _resourceIdProvider.Create;

                    if (!string.IsNullOrWhiteSpace(persistedId))
                    {
                        _resourceIdProvider.Create = () => persistedId;
                    }

                    await request.Handler.Invoke(httpContext);

                    _resourceIdProvider.Create = originalResourceIdProvider;

                    entryComponent = CreateEntryComponent(httpContext);
                }
                else
                {
                    entryComponent = new EntryComponent
                    {
                        Response = new ResponseComponent
                        {
                            Status = ((int)HttpStatusCode.NotFound).ToString(),
                            Outcome = CreateOperationOutcome(
                                OperationOutcome.IssueSeverity.Error,
                                OperationOutcome.IssueType.NotFound,
                                string.Format(Api.Resources.BundleNotFound, $"{request.HttpContext.Request.Path}{request.HttpContext.Request.QueryString}")),
                        },
                    };
                }

                if (_bundleType.Equals(BundleType.Transaction) && entryComponent.Response.Outcome != null)
                {
                    var errorMessage = string.Format(Api.Resources.TransactionFailed, request.HttpContext.Request.Method, request.HttpContext.Request.Path);

                    if (!Enum.TryParse(entryComponent.Response.Status, out HttpStatusCode httpStatusCode))
                    {
                        httpStatusCode = HttpStatusCode.BadRequest;
                    }

                    TransactionExceptionHandler.ThrowTransactionException(errorMessage, httpStatusCode, (OperationOutcome)entryComponent.Response.Outcome);
                }

                responseBundle.Entry[entryIndex] = entryComponent;
            }
        }

        private EntryComponent CreateEntryComponent(HttpContext httpContext)
        {
            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            string bodyContent = new StreamReader(httpContext.Response.Body).ReadToEnd();

            ResponseHeaders responseHeaders = httpContext.Response.GetTypedHeaders();

            var entryComponent = new EntryComponent
            {
                Response = new ResponseComponent
                {
                    Status = httpContext.Response.StatusCode.ToString(),
                    Location = responseHeaders.Location?.OriginalString,
                    Etag = responseHeaders.ETag?.ToString(),
                    LastModified = responseHeaders.LastModified,
                },
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
            else
            {
                if (httpContext.Response.StatusCode == (int)HttpStatusCode.Forbidden)
                {
                    entryComponent.Response.Outcome = CreateOperationOutcome(
                        OperationOutcome.IssueSeverity.Error,
                        OperationOutcome.IssueType.Forbidden,
                        Api.Resources.Forbidden);
                }
            }

            return entryComponent;
        }

        private void SetupContexts(RouteContext request, HttpContext httpContext)
        {
            IFhirRequestContext originalFhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            request.RouteData.Values.TryGetValue("controller", out object controllerName);
            request.RouteData.Values.TryGetValue("action", out object actionName);
            request.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);
            var newFhirRequestContext = new FhirRequestContext(
                httpContext.Request.Method,
                httpContext.Request.GetDisplayUrl(),
                originalFhirRequestContext.BaseUri.OriginalString,
                originalFhirRequestContext.CorrelationId,
                originalFhirRequestContext.QueryParameters,
                httpContext.Request.Headers,
                httpContext.Response.Headers)
            {
                Principal = originalFhirRequestContext.Principal,
                ResourceType = resourceType?.ToString(),
                AuditEventType = _auditEventTypeMapping.GetAuditEventType(
                    controllerName?.ToString(),
                    actionName?.ToString()),
            };

            _fhirRequestContextAccessor.FhirRequestContext = newFhirRequestContext;

            _bundleHttpContextAccessor.HttpContext = httpContext;
        }

        private void PopulateReferenceIdDictionary(IEnumerable<EntryComponent> bundleEntries, IDictionary<string, (string resourceId, string resourceType)> idDictionary)
        {
            foreach (EntryComponent entry in bundleEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullUrl))
                {
                    continue;
                }

                switch (entry.Request.Method)
                {
                    case HTTPVerb.PUT when entry.Request.Url.Contains('?', StringComparison.InvariantCultureIgnoreCase):
                    case HTTPVerb.POST:
                        if (!idDictionary.ContainsKey(entry.FullUrl))
                        {
                            // This id is new to us
                            var insertId = _resourceIdProvider.Create();
                            entry.Resource.Id = insertId;

                            idDictionary.Add(entry.FullUrl, (insertId, entry.Resource.TypeName));
                        }

                        break;
                }
            }
        }

        private static OperationOutcome CreateOperationOutcome(OperationOutcome.IssueSeverity issueSeverity, OperationOutcome.IssueType issueType, string diagnostics)
        {
            return new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = issueSeverity,
                        Code = issueType,
                        Diagnostics = diagnostics,
                    },
                },
            };
        }
    }
}
