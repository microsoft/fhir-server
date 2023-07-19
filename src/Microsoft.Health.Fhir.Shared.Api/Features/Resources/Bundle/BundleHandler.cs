// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
#if !STU3
using Microsoft.Health.Fhir.Api.Features.Formatters;
#endif
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Handler for bundles of type transaction and batch.
    /// </summary>
    public partial class BundleHandler : IRequestHandler<BundleRequest, BundleResponse>
    {
        private const BundleProcessingLogic DefaultBundleProcessingLogic = BundleProcessingLogic.Sequential;

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly Dictionary<HTTPVerb, List<ResourceExecutionContext>> _requests;
        private readonly IHttpAuthenticationFeature _httpAuthenticationFeature;
        private readonly IRouter _router;
        private readonly IServiceProvider _requestServices;
        private readonly ITransactionHandler _transactionHandler;
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly ILogger<BundleHandler> _logger;
        private readonly HTTPVerb[] _verbExecutionOrder;
        private readonly List<int> _emptyRequestsOrder;
        private readonly Dictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly string _originalRequestBase;
        private readonly IMediator _mediator;
        private readonly BundleProcessingLogic _bundleProcessingLogic;

        private int _requestCount;
        private BundleType? _bundleType;
        private bool _bundleProcessingTypeIsInvalid = false;

        /// <summary>
        /// Headers to propagate the the from the inner actions to the outer HTTP request.
        /// </summary>
        private static readonly string[] HeadersToAccumulate = new[] { KnownHeaders.RetryAfter, KnownHeaders.RetryAfterMilliseconds, "x-ms-session-token", "x-ms-request-charge" };

        private IFhirRequestContext _originalFhirRequestContext;

        public BundleHandler(
            IHttpContextAccessor httpContextAccessor,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            FhirJsonSerializer fhirJsonSerializer,
            FhirJsonParser fhirJsonParser,
            ITransactionHandler transactionHandler,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            IBundleOrchestrator bundleOrchestrator,
            ResourceIdProvider resourceIdProvider,
            TransactionBundleValidator transactionBundleValidator,
            ResourceReferenceResolver referenceResolver,
            IAuditEventTypeMapping auditEventTypeMapping,
            IOptions<BundleConfiguration> bundleConfiguration,
            IAuthorizationService<DataActions> authorizationService,
            IMediator mediator,
            ILogger<BundleHandler> logger)
            : this()
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));
            EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            EnsureArg.IsNotNull(transactionBundleValidator, nameof(transactionBundleValidator));
            EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));
            EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            EnsureArg.IsNotNull(bundleConfiguration?.Value, nameof(bundleConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonSerializer = fhirJsonSerializer;
            _fhirJsonParser = fhirJsonParser;
            _transactionHandler = transactionHandler;
            _bundleHttpContextAccessor = bundleHttpContextAccessor;
            _bundleOrchestrator = bundleOrchestrator;
            _resourceIdProvider = resourceIdProvider;
            _transactionBundleValidator = transactionBundleValidator;
            _referenceResolver = referenceResolver;
            _auditEventTypeMapping = auditEventTypeMapping;
            _authorizationService = authorizationService;
            _bundleConfiguration = bundleConfiguration.Value;
            _mediator = mediator;
            _logger = logger;

            // Not all versions support the same enum values, so do the dictionary creation in the version specific partial.
            _requests = _verbExecutionOrder.ToDictionary(verb => verb, _ => new List<ResourceExecutionContext>());

            HttpContext outerHttpContext = httpContextAccessor.HttpContext;
            _httpAuthenticationFeature = outerHttpContext.Features.Get<IHttpAuthenticationFeature>();
            _router = outerHttpContext.GetRouteData().Routers.First();
            _requestServices = outerHttpContext.RequestServices;
            _originalRequestBase = outerHttpContext.Request.PathBase;
            _emptyRequestsOrder = new List<int>();
            _referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            _bundleProcessingLogic = GetBundleProcessingLogic(outerHttpContext, _logger);
        }

        public async Task<BundleResponse> Handle(BundleRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // In scenarios where access checks involve a remote service call, it is advantageous
            // to perform one single access check for all necessary permissions rather than one per operation.
            // Two potential TODOs:
            // (1) it would also be better to know what the operations are in the bundle as opposed to checking
            //      for all possible actions. Trouble is, the exact mapping from method + URI is embedded in MVC logic
            //      and attributes.
            // (2) One we have the full set of permitted actions, it would be more efficient for the individual
            //     operations to use an IAuthorizationService<DataActions> that implements CheckAccess based on these known permitted
            //     actions.

            if (await _authorizationService.CheckAccess(DataActions.All, cancellationToken) == DataActions.None)
            {
                throw new UnauthorizedFhirActionException();
            }

            _originalFhirRequestContext = _fhirRequestContextAccessor.RequestContext;
            try
            {
                BundleProcessingLogic processingLogic = (_bundleOrchestrator.IsEnabled && _bundleProcessingLogic == BundleProcessingLogic.Parallel) ? BundleProcessingLogic.Parallel : BundleProcessingLogic.Sequential;

                var bundleResource = request.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
                _bundleType = bundleResource.Type;

                if (_bundleType == BundleType.Batch)
                {
                    await FillRequestLists(bundleResource.Entry, cancellationToken);

                    var responseBundle = new Hl7.Fhir.Model.Bundle
                    {
                        Type = BundleType.BatchResponse,
                    };

                    await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, processingLogic, cancellationToken);

                    var response = new BundleResponse(responseBundle.ToResourceElement());

                    await PublishNotification(responseBundle, BundleType.Batch);

                    return response;
                }
                else if (_bundleType == BundleType.Transaction)
                {
                    // For resources within a transaction, we need to validate if they are referring to each other and throw an exception in such case.
                    await _transactionBundleValidator.ValidateBundle(bundleResource, _referenceIdDictionary, cancellationToken);

                    await FillRequestLists(bundleResource.Entry, cancellationToken);

                    var responseBundle = new Hl7.Fhir.Model.Bundle
                    {
                        Type = BundleType.TransactionResponse,
                    };

                    var response = await ExecuteTransactionForAllRequestsAsync(responseBundle, processingLogic, cancellationToken);

                    await PublishNotification(responseBundle, BundleType.Transaction);

                    return response;
                }
                else
                {
                    throw new MethodNotAllowedException(string.Format(Api.Resources.InvalidBundleType, _bundleType));
                }
            }
            finally
            {
                _fhirRequestContextAccessor.RequestContext = _originalFhirRequestContext;
            }
        }

        private BundleProcessingLogic GetBundleProcessingLogic(HttpContext outerHttpContext, ILogger<BundleHandler> logger)
        {
            if (outerHttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    return DefaultBundleProcessingLogic;
                }

                try
                {
                    BundleProcessingLogic processingLogic = (BundleProcessingLogic)Enum.Parse(typeof(BundleProcessingLogic), processingLogicAsString.Trim(), ignoreCase: true);
                    return processingLogic;
                }
                catch (Exception e)
                {
                    _bundleProcessingTypeIsInvalid = true;
                    logger.LogWarning(e, "Error while extracting the Bundle Processing Logic out of the HTTP Header: {ErrorMessage}", e.Message);

                    return DefaultBundleProcessingLogic;
                }
            }

            return DefaultBundleProcessingLogic;
        }

        private async Task ProcessAllResourcesInABundleAsRequestsAsync(Hl7.Fhir.Model.Bundle responseBundle, BundleProcessingLogic processingLogic, CancellationToken cancellationToken)
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

            BundleHandlerStatistics statistics = CreateNewBundleHandlerStatistics(processingLogic);
            try
            {
                if (processingLogic == BundleProcessingLogic.Sequential)
                {
                    // This logic is applicable for sequential batches and transactions.

                    // Batches and transactions will follow the same logic, and process resources split by HTTPVerb groups.
                    EntryComponent throttledEntryComponent = null;
                    foreach (HTTPVerb verb in _verbExecutionOrder)
                    {
                        throttledEntryComponent = await ExecuteRequestsWithSingleHttpVerbInSequenceAsync(
                            responseBundle: responseBundle,
                            httpVerb: verb,
                            throttledEntryComponent: throttledEntryComponent,
                            statistics: statistics,
                            cancellationToken: cancellationToken);
                    }
                }
                else if (processingLogic == BundleProcessingLogic.Parallel && _bundleType == BundleType.Batch)
                {
                    // Besides the need to run requests in parallel, the verb execution order should still be respected.
                    EntryComponent throttledEntryComponent = null;
                    foreach (HTTPVerb verb in _verbExecutionOrder)
                    {
                        IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.CreateNewOperation(
                            type: BundleOrchestratorOperationType.Batch,
                            label: verb.ToString(),
                            expectedNumberOfResources: _requests[verb].Count);

                        _logger.LogTrace(
                            "BundleHandler - Starting the parallel processing of {NumberOfRequests} '{HttpVerb}' requests.",
                            bundleOperation.OriginalExpectedNumberOfResources,
                            verb);

                        throttledEntryComponent = await ExecuteRequestsWithSingleHttpVerbInParallelAsync(
                            responseBundle: responseBundle,
                            resources: _requests[verb],
                            bundleOperation: bundleOperation,
                            throttledEntryComponent: throttledEntryComponent,
                            statistics: statistics,
                            cancellationToken: cancellationToken);
                    }
                }
                else if (processingLogic == BundleProcessingLogic.Parallel && _bundleType == BundleType.Transaction)
                {
                    List<ResourceExecutionContext> resources = _requests.Select(r => r.Value).SelectMany(r => r).ToList();

                    IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.CreateNewOperation(
                        type: BundleOrchestratorOperationType.Transaction,
                        label: "Transaction",
                        expectedNumberOfResources: resources.Count);

                    _logger.LogTrace(
                        "BundleHandler - Starting the parallel processing of a transaction with {NumberOfRequests} requests.",
                        bundleOperation.OriginalExpectedNumberOfResources);

                    EntryComponent throttledEntryComponent = await ExecuteRequestsWithSingleHttpVerbInParallelAsync(
                        responseBundle: responseBundle,
                        resources: resources,
                        bundleOperation: bundleOperation,
                        throttledEntryComponent: null,
                        statistics: statistics,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(string.Format(Api.Resources.BundleInvalidCombination, _bundleType, processingLogic));
                }
            }
            finally
            {
                FinishCollectingBundleStatistics(statistics);
            }

            AddFinalOperationOutcomesIfApplicable(responseBundle);
        }

        private void AddFinalOperationOutcomesIfApplicable(Hl7.Fhir.Model.Bundle responseBundle)
        {
            try
            {
                if (_bundleProcessingTypeIsInvalid)
                {
                    var entryComponent = new EntryComponent
                    {
                        Response = new ResponseComponent
                        {
                            Status = ((int)HttpStatusCode.MethodNotAllowed).ToString(),
                            Outcome = CreateOperationOutcome(
                                OperationOutcome.IssueSeverity.Warning,
                                OperationOutcome.IssueType.Invalid,
                                $"The bundle processing logic provided was invalid. The bundle was processed using {DefaultBundleProcessingLogic} processing."),
                        },
                    };

                    responseBundle.Entry.Add(entryComponent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error including additional operation outcomes. This error will not block the bundle processing.");
            }
        }

        private async Task PublishNotification(Hl7.Fhir.Model.Bundle responseBundle, BundleType bundleType)
        {
            var apiCallResults = new Dictionary<string, List<BundleSubCallMetricData>>();
            foreach (var entry in responseBundle.Entry)
            {
                var status = entry.Response.Status;
                if (!apiCallResults.TryGetValue(status, out List<BundleSubCallMetricData> val))
                {
                    apiCallResults[status] = new List<BundleSubCallMetricData>();
                }

                apiCallResults[status].Add(new BundleSubCallMetricData()
                {
                    FhirOperation = "Bundle Sub Call",
                    ResourceType = entry?.Resource?.TypeName,
                });
            }

            await _mediator.Publish(new BundleMetricsNotification(apiCallResults, bundleType == BundleType.Batch ? AuditEventSubType.Batch : AuditEventSubType.Transaction), CancellationToken.None);
        }

        private async Task<BundleResponse> ExecuteTransactionForAllRequestsAsync(Hl7.Fhir.Model.Bundle responseBundle, BundleProcessingLogic processingLogic, CancellationToken cancellationToken)
        {
            try
            {
                if (processingLogic == BundleProcessingLogic.Sequential)
                {
                    using (var transaction = _transactionHandler.BeginTransaction())
                    {
                        await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, processingLogic, cancellationToken);
                        transaction.Complete();
                    }
                }
                else
                {
                    await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, processingLogic, cancellationToken);
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

            var requestUri = new Uri(_fhirRequestContextAccessor.RequestContext.BaseUri, requestUrl);
            httpContext.Request.Scheme = requestUri.Scheme;
            httpContext.Request.Host = new HostString(requestUri.Host, requestUri.Port);
            httpContext.Request.PathBase = _originalRequestBase;
            httpContext.Request.Path = requestUri.LocalPath;
            httpContext.Request.QueryString = new QueryString(requestUri.Query);
            httpContext.Request.Method = requestMethod.ToString();

            AddHeaderIfNeeded(HeaderNames.IfMatch, entry.Request.IfMatch, httpContext);
            AddHeaderIfNeeded(HeaderNames.IfModifiedSince, entry.Request.IfModifiedSince?.ToString(), httpContext);
            AddHeaderIfNeeded(HeaderNames.IfNoneMatch, entry.Request.IfNoneMatch, httpContext);
            AddHeaderIfNeeded(KnownHeaders.IfNoneExist, entry.Request.IfNoneExist, httpContext);

            if (_fhirRequestContextAccessor.RequestContext.RequestHeaders.TryGetValue(KnownHeaders.ProfileValidation, out var profileValidationValue))
            {
                AddHeaderIfNeeded(KnownHeaders.ProfileValidation, profileValidationValue, httpContext);
            }

            if (_fhirRequestContextAccessor.RequestContext.RequestHeaders.TryGetValue(KnownHeaders.Prefer, out var preferValue))
            {
                AddHeaderIfNeeded(KnownHeaders.Prefer, preferValue, httpContext);
            }

            if (requestMethod == HTTPVerb.POST
                || requestMethod == HTTPVerb.PUT)
            {
                httpContext.Request.Headers.Add(HeaderNames.ContentType, new StringValues(KnownContentTypes.JsonContentType));

                if (entry.Resource != null)
                {
                    var memoryStream = new MemoryStream(await _fhirJsonSerializer.SerializeToBytesAsync(entry.Resource));
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    httpContext.Request.Body = memoryStream;
                }
            }

#if !STU3
            // FHIRPatch if body is Parameters object
            else if (
                requestMethod == HTTPVerb.PATCH &&
                string.Equals(KnownResourceTypes.Parameters, entry.Resource?.TypeName, StringComparison.Ordinal) &&
                entry.Resource is Parameters parametersResource)
            {
                httpContext.Request.Headers.Add(HeaderNames.ContentType, new StringValues(KnownContentTypes.JsonContentType));
                var memoryStream = new MemoryStream(await _fhirJsonSerializer.SerializeToBytesAsync(parametersResource));
                memoryStream.Seek(0, SeekOrigin.Begin);
                httpContext.Request.Body = memoryStream;
            }

            // Allow JSON Patch to be an encoded Binary in a Bundle (See: https://chat.fhir.org/#narrow/stream/179166-implementers/topic/Transaction.20with.20PATCH.20request)
            else if (
                requestMethod == HTTPVerb.PATCH &&
                string.Equals(KnownResourceTypes.Binary, entry.Resource?.TypeName, StringComparison.Ordinal) &&
                entry.Resource is Binary binaryResource && string.Equals(KnownMediaTypeHeaderValues.ApplicationJsonPatch.ToString(), binaryResource.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Request.Headers.Add(HeaderNames.ContentType, new StringValues(binaryResource.ContentType));
                var memoryStream = new MemoryStream(binaryResource.Data);
                memoryStream.Seek(0, SeekOrigin.Begin);
                httpContext.Request.Body = memoryStream;
            }
#endif

            var routeContext = new RouteContext(httpContext);

            await _router.RouteAsync(routeContext);

            httpContext.Features[typeof(IRoutingFeature)] = new RoutingFeature
            {
                RouteData = routeContext.RouteData,
            };

            _requests[requestMethod].Add(new ResourceExecutionContext(requestMethod, routeContext, order, persistedId));
        }

        private static void AddHeaderIfNeeded(string headerKey, string headerValue, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                httpContext.Request.Headers.Add(headerKey, new StringValues(headerValue));
            }
        }

        private async Task<EntryComponent> ExecuteRequestsWithSingleHttpVerbInSequenceAsync(Hl7.Fhir.Model.Bundle responseBundle, HTTPVerb httpVerb, EntryComponent throttledEntryComponent, BundleHandlerStatistics statistics, CancellationToken cancellationToken)
        {
            const int GCCollectTrigger = 150;

            foreach (ResourceExecutionContext resourceContext in _requests[httpVerb])
            {
                if (resourceContext.Index % GCCollectTrigger == 0 && resourceContext.Index > 0)
                {
                    RunGarbageCollection();
                }

                EntryComponent entryComponent;

                Stopwatch watch = Stopwatch.StartNew();
                if (resourceContext.Context.Handler != null)
                {
                    if (throttledEntryComponent != null)
                    {
                        // A previous action was throttled.
                        // Skip executing subsequent actions and include the 429 response.
                        entryComponent = throttledEntryComponent;
                    }
                    else
                    {
                        HttpContext httpContext = resourceContext.Context.HttpContext;

                        SetupContexts(resourceContext.Context, httpContext);

                        Func<string> originalResourceIdProvider = _resourceIdProvider.Create;

                        if (!string.IsNullOrWhiteSpace(resourceContext.PersistedId))
                        {
                            _resourceIdProvider.Create = () => resourceContext.PersistedId;
                        }

                        await resourceContext.Context.Handler.Invoke(httpContext);

                        // we will retry a 429 one time per request in the bundle
                        if (httpContext.Response.StatusCode == (int)HttpStatusCode.TooManyRequests)
                        {
                            _logger.LogTrace("BundleHandler received 429 message, attempting retry.  HttpVerb:{HttpVerb} BundleSize: {RequestCount} entryIndex:{EntryIndex}", httpVerb, _requestCount, resourceContext.Index);
                            int retryDelay = 2;
                            var retryAfterValues = httpContext.Response.Headers.GetCommaSeparatedValues("Retry-After");
                            if (retryAfterValues != StringValues.Empty && int.TryParse(retryAfterValues[0], out var retryHeaderValue))
                            {
                                retryDelay = retryHeaderValue;
                            }

                            await Task.Delay(retryDelay * 1000, cancellationToken); // multiply by 1000 as retry-header specifies delay in seconds
                            await resourceContext.Context.Handler.Invoke(httpContext);
                        }

                        _resourceIdProvider.Create = originalResourceIdProvider;

                        entryComponent = CreateEntryComponent(_fhirJsonParser, httpContext);

                        foreach (string headerName in HeadersToAccumulate)
                        {
                            if (httpContext.Response.Headers.TryGetValue(headerName, out var values))
                            {
                                _originalFhirRequestContext.ResponseHeaders[headerName] = values;
                            }
                        }

                        if (_bundleType == BundleType.Batch && entryComponent.Response.Status == "429")
                        {
                            _logger.LogTrace("BundleHandler received 429 after retry, now aborting remainder of bundle. HttpVerb:{HttpVerb} BundleSize: {RequestCount} entryIndex:{EntryIndex}", httpVerb, _requestCount, resourceContext.Index);

                            // this action was throttled. Capture the entry and reuse it for subsequent actions.
                            throttledEntryComponent = entryComponent;
                        }
                    }
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
                                string.Format(Api.Resources.BundleNotFound, $"{resourceContext.Context.HttpContext.Request.Path}{resourceContext.Context.HttpContext.Request.QueryString}")),
                        },
                    };
                }

                statistics.RegisterNewEntry(httpVerb, resourceContext.Index, entryComponent.Response.Status, watch.Elapsed);

                if (_bundleType.Equals(BundleType.Transaction) && entryComponent.Response.Outcome != null)
                {
                    var errorMessage = string.Format(Api.Resources.TransactionFailed, resourceContext.Context.HttpContext.Request.Method, resourceContext.Context.HttpContext.Request.Path);

                    if (!Enum.TryParse(entryComponent.Response.Status, out HttpStatusCode httpStatusCode))
                    {
                        httpStatusCode = HttpStatusCode.BadRequest;
                    }

                    TransactionExceptionHandler.ThrowTransactionException(errorMessage, httpStatusCode, (OperationOutcome)entryComponent.Response.Outcome);
                }

                responseBundle.Entry[resourceContext.Index] = entryComponent;
           }

            return throttledEntryComponent;
        }

        private static EntryComponent CreateEntryComponent(FhirJsonParser fhirJsonParser, HttpContext httpContext)
        {
            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(httpContext.Response.Body);
            string bodyContent = reader.ReadToEnd();

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
                var entryComponentResource = fhirJsonParser.Parse<Resource>(bodyContent);

                if (entryComponentResource.TypeName == KnownResourceTypes.OperationOutcome)
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
            request.RouteData.Values.TryGetValue("controller", out object controllerName);
            request.RouteData.Values.TryGetValue("action", out object actionName);
            request.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);
            var newFhirRequestContext = new FhirRequestContext(
                httpContext.Request.Method,
                httpContext.Request.GetDisplayUrl(),
                _originalFhirRequestContext.BaseUri.OriginalString,
                _originalFhirRequestContext.CorrelationId,
                httpContext.Request.Headers,
                httpContext.Response.Headers)
            {
                Principal = _originalFhirRequestContext.Principal,
                ResourceType = resourceType?.ToString(),
                AuditEventType = _auditEventTypeMapping.GetAuditEventType(
                    controllerName?.ToString(),
                    actionName?.ToString()),
                ExecutingBatchOrTransaction = true,
                AccessControlContext = _originalFhirRequestContext.AccessControlContext.Clone() as AccessControlContext,
            };
            foreach (var scopeRestriction in _originalFhirRequestContext.AccessControlContext.AllowedResourceActions)
            {
                newFhirRequestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            newFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = _originalFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl;
            _fhirRequestContextAccessor.RequestContext = newFhirRequestContext;

            _bundleHttpContextAccessor.HttpContext = httpContext;

            foreach (string headerName in HeadersToAccumulate)
            {
                if (_originalFhirRequestContext.ResponseHeaders.TryGetValue(headerName, out var values))
                {
                    newFhirRequestContext.ResponseHeaders.Add(headerName, values);
                }
            }
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

        private void RunGarbageCollection()
        {
            try
            {
                _logger.LogTrace("{Origin} - MemoryWatch - Memory used before collection: {MemoryInUse:N0}", nameof(BundleHandler), GC.GetTotalMemory(forceFullCollection: false));

                // Collecting memory up to Generation 2 using default collection mode.
                // No blocking, allowing a collection to be performed as soon as possible, if another collection is not in progress.
                // SOH compacting is set to true.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, blocking: false, compacting: true);

                _logger.LogTrace("{Origin} - MemoryWatch - Memory used after full collection: {MemoryInUse:N0}", nameof(BundleHandler), GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Origin} - MemoryWatch - Error running garbage collection.", nameof(BundleHandler));
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

        private BundleHandlerStatistics CreateNewBundleHandlerStatistics(BundleProcessingLogic processingLogic)
        {
            BundleHandlerStatistics statistics = new BundleHandlerStatistics(_bundleType, processingLogic, _requestCount);

            statistics.StartCollectingResults();

            return statistics;
        }

        private void FinishCollectingBundleStatistics(BundleHandlerStatistics statistics)
        {
            statistics.StopCollectingResults();

            try
            {
                string statisticsAsJson = statistics.GetStatisticsAsJson();
                _logger.LogTrace(statisticsAsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error computing bundle statistics. This error will not block the bundle processing.");
            }
        }
    }
}
