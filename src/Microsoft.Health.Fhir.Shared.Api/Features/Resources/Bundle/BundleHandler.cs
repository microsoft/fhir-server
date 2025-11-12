// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
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
using Medino;
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
using Microsoft.Health.Fhir.Api.Features.Headers;
#if !STU3
using Microsoft.Health.Fhir.Api.Features.Formatters;
#endif
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json.Linq;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Handler for bundles of type transaction and batch.
    /// </summary>
    public partial class BundleHandler : IRequestHandler<BundleRequest, BundleResponse>
    {
        private readonly HttpContext _outerHttpContext;
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
        private readonly IProvideProfilesForValidation _profilesResolver;
        private readonly ILogger<BundleHandler> _logger;
        private readonly HTTPVerb[] _verbExecutionOrder;
        private readonly List<int> _emptyRequestsOrder;
        private readonly ConcurrentDictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;
        private readonly TransactionBundleValidator _transactionBundleValidator;
        private readonly ResourceReferenceResolver _referenceResolver;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly string _originalRequestBase;
        private readonly IMediator _mediator;
        private readonly bool _optimizedQuerySet;
        private readonly bool _isBundleProcessingLogicValid;

        // Total number of requests in the bundle.
        private int _requestCount;

        // Bundle type being processed (batch, transaction).
        private BundleType? _bundleType;

        // Indicates if any of the resources in the bundle require Profile refresh.
        private bool _forceProfilesRefresh = false;

        // Total number of generated IDs in the bundle.
        private int _totalGeneratedIdentifiers = 0;

        // Total number of resolved references in the bundle.
        private int _totalResolvedReferences = 0;

        /// <summary>
        /// Headers to propagate from the inner actions to the outer HTTP request.
        /// </summary>
        private static readonly string[] HeadersToAccumulate = new[] { KnownHeaders.RetryAfter, KnownHeaders.RetryAfterMilliseconds, "x-ms-session-token", "x-ms-request-charge" };

        /// <summary>
        /// Properties to propagate from the outer HTTP requests to the inner actions.
        /// </summary>
        private static readonly string[] PropertiesToAccumulate = new[] { KnownQueryParameterNames.OptimizeConcurrency };

        private static readonly Uri LocalHost = new("http://localhost/");

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
            IRouter router,
            IProvideProfilesForValidation profilesResolver,
            ILogger<BundleHandler> logger)
            : this()
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            _fhirRequestContextAccessor = EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            _fhirJsonSerializer = EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            _fhirJsonParser = EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            _transactionHandler = EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));
            _bundleHttpContextAccessor = EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));
            _bundleOrchestrator = EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            _resourceIdProvider = EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            _transactionBundleValidator = EnsureArg.IsNotNull(transactionBundleValidator, nameof(transactionBundleValidator));
            _referenceResolver = EnsureArg.IsNotNull(referenceResolver, nameof(referenceResolver));
            _auditEventTypeMapping = EnsureArg.IsNotNull(auditEventTypeMapping, nameof(auditEventTypeMapping));
            _bundleConfiguration = EnsureArg.IsNotNull(bundleConfiguration?.Value, nameof(bundleConfiguration));
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _router = EnsureArg.IsNotNull(router, nameof(router));
            _profilesResolver = EnsureArg.IsNotNull(profilesResolver, nameof(profilesResolver));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            // Not all versions support the same enum values, so do the dictionary creation in the version specific partial.
            _requests = _verbExecutionOrder.ToDictionary(verb => verb, _ => new List<ResourceExecutionContext>());

            _outerHttpContext = httpContextAccessor.HttpContext;
            _httpAuthenticationFeature = _outerHttpContext.Features.Get<IHttpAuthenticationFeature>();
            _requestServices = _outerHttpContext.RequestServices;
            _originalRequestBase = _outerHttpContext.Request.PathBase;
            _emptyRequestsOrder = new List<int>();
            _referenceIdDictionary = new ConcurrentDictionary<string, (string resourceId, string resourceType)>();

            // Set optimized-query processing logic.
            _optimizedQuerySet = SetRequestContextWithOptimizedQuerying(_outerHttpContext, fhirRequestContextAccessor.RequestContext, _logger);

            _isBundleProcessingLogicValid = _bundleOrchestrator.IsEnabled ? BundleHandlerRuntime.IsBundleProcessingLogicValid(_outerHttpContext) : true;
        }

        public async Task<BundleResponse> HandleAsync(BundleRequest request, CancellationToken cancellationToken)
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
                Stopwatch stopwatch = Stopwatch.StartNew();

                var bundleResource = request.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
                _bundleType = bundleResource.Type;

                // Retrieve bundle processing logic.
                BundleProcessingLogic bundleProcessingLogic = _bundleOrchestrator.IsEnabled ? BundleHandlerRuntime.GetBundleProcessingLogic(_bundleConfiguration, _outerHttpContext, _bundleType) : BundleProcessingLogic.Sequential;

                if (_bundleType == BundleType.Batch)
                {
                    await FillRequestLists(bundleResource.Entry, cancellationToken);

                    var responseBundle = new Hl7.Fhir.Model.Bundle
                    {
                        Type = BundleType.BatchResponse,
                    };

                    await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, bundleProcessingLogic, cancellationToken);

                    var response = new BundleResponse(
                        responseBundle.ToResourceElement(),
                        new BundleResponseInfo(stopwatch.Elapsed, BundleType.Batch, bundleProcessingLogic));

                    await PublishNotification(responseBundle, BundleType.Batch);

                    return response;
                }
                else if (_bundleType == BundleType.Transaction)
                {
                    // For resources within a transaction, we need to validate if they are referring to each other and throw an exception in such case.
                    await _transactionBundleValidator.ValidateBundle(bundleResource, _referenceIdDictionary, cancellationToken);

                    await FillRequestLists(bundleResource.Entry, cancellationToken);

                    if (bundleProcessingLogic == BundleProcessingLogic.Sequential && _requestCount <= 1)
                    {
                        // In this scenario, if the transactional bundle contains a single element, then the execution is forced to be done as parallel.
                        _logger.LogInformation("Edge Case scenario: sequential transactional bundle has a single record, and it's now changed to execute as parallel.");
                        bundleProcessingLogic = BundleProcessingLogic.Parallel;
                    }

                    var responseBundle = new Hl7.Fhir.Model.Bundle
                    {
                        Type = BundleType.TransactionResponse,
                    };

                    await ExecuteTransactionForAllRequestsAsync(responseBundle, bundleProcessingLogic, cancellationToken);

                    var response = new BundleResponse(
                        responseBundle.ToResourceElement(),
                        new BundleResponseInfo(stopwatch.Elapsed, BundleType.Transaction, bundleProcessingLogic));

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

        private static bool SetRequestContextWithOptimizedQuerying(HttpContext outerHttpContext, IFhirRequestContext fhirRequestContext, ILogger<BundleHandler> logger)
        {
            try
            {
                ConditionalQueryProcessingLogic conditionalQueryProcessingLogic = outerHttpContext.GetConditionalQueryProcessingLogic();

                if (conditionalQueryProcessingLogic == ConditionalQueryProcessingLogic.Parallel)
                {
                    fhirRequestContext.DecorateRequestContextWithOptimizedConcurrency();
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error while extracting the Conditional-Query Processing Logic out of the HTTP Header: {ErrorMessage}", e.Message);
            }

            return false;
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
                        if (_requests[verb].Any())
                        {
                            throttledEntryComponent = await ExecuteRequestsWithSingleHttpVerbInSequenceAsync(
                                responseBundle: responseBundle,
                                httpVerb: verb,
                                throttledEntryComponent: throttledEntryComponent,
                                statistics: statistics,
                                cancellationToken: cancellationToken);
                        }
                    }
                }
                else if (processingLogic == BundleProcessingLogic.Parallel && _bundleType == BundleType.Batch)
                {
                    // Besides the need to run requests in parallel, the verb execution order should still be respected.
                    EntryComponent throttledEntryComponent = null;
                    foreach (HTTPVerb verb in _verbExecutionOrder)
                    {
                        if (_requests[verb].Any())
                        {
                            IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.CreateNewOperation(
                                type: BundleOrchestratorOperationType.Batch,
                                label: verb.ToString(),
                                expectedNumberOfResources: _requests[verb].Count);

                            _logger.LogInformation(
                                "BundleHandler - Starting the parallel processing of {NumberOfRequests} '{HttpVerb}' requests.",
                                bundleOperation.OriginalExpectedNumberOfResources,
                                verb);

                            throttledEntryComponent = await ExecuteRequestsInParallelAsync(
                                responseBundle: responseBundle,
                                resources: _requests[verb],
                                bundleOperation: bundleOperation,
                                throttledEntryComponent: throttledEntryComponent,
                                statistics: statistics,
                                cancellationToken: cancellationToken);
                        }
                    }
                }
                else if (processingLogic == BundleProcessingLogic.Parallel && _bundleType == BundleType.Transaction)
                {
                    List<ResourceExecutionContext> resources = _requests.Select(r => r.Value).SelectMany(r => r).ToList();

                    if (resources.Any())
                    {
                        IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.CreateNewOperation(
                            type: BundleOrchestratorOperationType.Transaction,
                            label: "Transaction",
                            expectedNumberOfResources: resources.Count);

                        _logger.LogInformation(
                            "BundleHandler - Starting the parallel processing of a transaction with {NumberOfRequests} requests.",
                            bundleOperation.OriginalExpectedNumberOfResources);

                        EntryComponent throttledEntryComponent = await ExecuteRequestsInParallelAsync(
                            responseBundle: responseBundle,
                            resources: resources,
                            bundleOperation: bundleOperation,
                            throttledEntryComponent: null,
                            statistics: statistics,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    throw new InvalidOperationException(string.Format(Api.Resources.BundleInvalidCombination, _bundleType, processingLogic));
                }
            }
            catch (FhirTransactionFailedException tfe) when (tfe.IsErrorCausedDueClientFailure())
            {
                _logger.LogWarning(tfe, "Client failure while processing a transaction bundle: {ErrorMessage}.", tfe.Message);
                statistics.MarkBundleAsFailedDueClientError();
                throw;
            }
            catch (FhirTransactionCancelledException tce)
            {
                _logger.LogWarning(tce, "Cancelled operation while processing a transaction bundle: {ErrorMessage}.", tce.Message);
                statistics.MarkBundleAsCancelled();
                throw;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Operation cancelled. Error while processing a bundle: {ErrorMessage}.", ex.Message);
                    statistics.MarkBundleAsCancelled();
                }
                else
                {
                    _logger.LogError(ex, "Error while processing a bundle: {ErrorMessage}.", ex.Message);
                }

                throw;
            }
            finally
            {
                FinishCollectingBundleStatistics(statistics);

                RefreshProfilesIfApplicable();
            }

            AddFinalOperationOutcomesIfApplicable(responseBundle, _bundleType);
        }

        private void AddFinalOperationOutcomesIfApplicable(Hl7.Fhir.Model.Bundle responseBundle, BundleType? bundleType)
        {
            try
            {
                if (!_isBundleProcessingLogicValid)
                {
                    var entryComponent = new EntryComponent
                    {
                        Response = new ResponseComponent
                        {
                            Status = ((int)HttpStatusCode.MethodNotAllowed).ToString(),
                            Outcome = CreateOperationOutcome(
                                OperationOutcome.IssueSeverity.Warning,
                                OperationOutcome.IssueType.Invalid,
                                $"The bundle processing logic provided was invalid. The bundle was processed using the default processing logic."),
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

            await _mediator.PublishAsync(new BundleMetricsNotification(apiCallResults, bundleType == BundleType.Batch ? AuditEventSubType.Batch : AuditEventSubType.Transaction, _outerHttpContext.Request.Scheme), CancellationToken.None);
        }

        private async Task ExecuteTransactionForAllRequestsAsync(Hl7.Fhir.Model.Bundle responseBundle, BundleProcessingLogic processingLogic, CancellationToken cancellationToken)
        {
            try
            {
                if (processingLogic == BundleProcessingLogic.Sequential)
                {
                    using (var transaction = _transactionHandler.BeginTransaction())
                    {
                        await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, BundleProcessingLogic.Sequential, cancellationToken);
                        transaction.Complete();
                    }
                }
                else
                {
                    await ProcessAllResourcesInABundleAsRequestsAsync(responseBundle, BundleProcessingLogic.Parallel, cancellationToken);
                }
            }
            catch (InvalidOperationException ioe) when (ioe.IsCompletedTransactionException())
            {
                _logger.LogError(ioe, "Failed to commit a transaction. This SqlTransaction has completed.");
                throw new FhirTransactionFailedException(Api.Resources.GeneralTransactionFailedError, HttpStatusCode.InternalServerError);
            }
            catch (TransactionAbortedException tae)
            {
                _logger.LogError(tae, "Failed to commit a transaction. Throwing BadRequest as a default exception.");
                throw new FhirTransactionFailedException(Api.Resources.GeneralTransactionFailedError, HttpStatusCode.BadRequest);
            }
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
            DefaultHttpContext httpContext = new DefaultHttpContext { RequestServices = _requestServices };

            var requestUrl = entry.Request?.Url;

            // For resources within a transaction, we need to resolve any intra-bundle references and potentially persist any internally assigned ids
            HTTPVerb requestMethod = entry.Request.Method.Value;

            if (_bundleType == BundleType.Transaction && entry.Resource != null)
            {
                int totalResolvedReferences = await _referenceResolver.ResolveReferencesAsync(entry.Resource, _referenceIdDictionary, requestUrl, cancellationToken);
                _totalResolvedReferences += totalResolvedReferences;

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

            string path = new Uri(LocalHost, requestUrl).GetComponents(UriComponents.Path, UriFormat.Unescaped);
            Uri requestUri = new(_fhirRequestContextAccessor.RequestContext.BaseUri, requestUrl);
            httpContext.Request.Scheme = requestUri.Scheme;
            httpContext.Request.Host = new HostString(requestUri.Host, requestUri.Port);
            httpContext.Request.PathBase = _originalRequestBase;
            httpContext.Request.Path = path.Length is 0 || path[0] is not '/' ? new PathString('/' + path) : new PathString(path);
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

            if (requestMethod == HTTPVerb.POST || requestMethod == HTTPVerb.PUT)
            {
                httpContext.Request.Headers[HeaderNames.ContentType] = new StringValues(KnownContentTypes.JsonContentType);

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
                httpContext.Request.Headers[HeaderNames.ContentType] = new StringValues(KnownContentTypes.JsonContentType);
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
                httpContext.Request.Headers[HeaderNames.ContentType] = new StringValues(binaryResource.ContentType);
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

            _requests[requestMethod].Add(new ResourceExecutionContext(requestMethod, entry.Resource?.TypeName, routeContext, order, persistedId));
        }

        private static void AddHeaderIfNeeded(string headerKey, string headerValue, DefaultHttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                httpContext.Request.Headers[headerKey] = new StringValues(headerValue);
            }
        }

        private async Task<EntryComponent> ExecuteRequestsWithSingleHttpVerbInSequenceAsync(Hl7.Fhir.Model.Bundle responseBundle, HTTPVerb httpVerb, EntryComponent throttledEntryComponent, BundleHandlerStatistics statistics, CancellationToken cancellationToken)
        {
            foreach (ResourceExecutionContext resourceContext in _requests[httpVerb])
            {
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

                        // Ensure to pass original callers cancellation token to honor client request cancellations and httprequest timeouts
                        httpContext.RequestAborted = cancellationToken;
                        SetupContexts(resourceContext, httpContext);

                        Func<string> originalResourceIdProvider = _resourceIdProvider.Create;

                        if (!string.IsNullOrWhiteSpace(resourceContext.PersistedId))
                        {
                            _resourceIdProvider.Create = () => resourceContext.PersistedId;
                        }

                        await resourceContext.Context.Handler.Invoke(httpContext);

                        // we will retry a 429 one time per request in the bundle
                        if (httpContext.Response.StatusCode == (int)HttpStatusCode.TooManyRequests)
                        {
                            _logger.LogWarning("BundleHandler received 429 message, attempting retry.  HttpVerb:{HttpVerb} BundleSize: {RequestCount} entryIndex:{EntryIndex}", httpVerb, _requestCount, resourceContext.Index);
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

                        DetectNeedToRefreshProfiles(resourceContext.ResourceType);

                        foreach (string headerName in HeadersToAccumulate)
                        {
                            if (httpContext.Response.Headers.TryGetValue(headerName, out var values))
                            {
                                _originalFhirRequestContext.ResponseHeaders[headerName] = values;
                            }
                        }

                        if (_bundleType == BundleType.Batch && entryComponent.Response.Status == "429")
                        {
                            _logger.LogInformation("BundleHandler received 429 after retry, now aborting remainder of bundle. HttpVerb:{HttpVerb} BundleSize: {RequestCount} entryIndex:{EntryIndex}", httpVerb, _requestCount, resourceContext.Index);

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

                statistics.RegisterNewEntry(httpVerb, resourceContext.ResourceType, resourceContext.Index, entryComponent.Response.Status, watch.Elapsed);

                if (_bundleType.Equals(BundleType.Transaction) && entryComponent.Response.Outcome != null)
                {
                    if (!Enum.TryParse(entryComponent.Response.Status, out HttpStatusCode httpStatusCode))
                    {
                        httpStatusCode = HttpStatusCode.BadRequest;
                    }

                    var errorMessage = string.Format(Api.Resources.TransactionFailed, resourceContext.Context.HttpContext.Request.Method, resourceContext.Context.HttpContext.Request.Path);

                    TransactionExceptionHandler.ThrowTransactionException(
                        errorMessage,
                        httpStatusCode,
                        (OperationOutcome)entryComponent.Response.Outcome,
                        cancelled: BundleHandlerRuntime.IsTransactionCancelledByClient(watch.Elapsed, _bundleConfiguration, cancellationToken));
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

        /// <summary>
        /// Reference implementation of 'SetupContexts'. Originally created to support sequential operations and run manipulations on local
        /// attributes. This is a non-thread safe method. Not to be used in parallel-operation under the same HTTP request context.
        /// </summary>
        private void SetupContexts(ResourceExecutionContext resourceExecutionContext, HttpContext httpContext)
        {
            SetupContexts(
                bundleType: _bundleType,
                request: resourceExecutionContext.Context,
                httpVerb: resourceExecutionContext.HttpVerb,
                persistedId: resourceExecutionContext.PersistedId,
                httpContext: httpContext,
                processingLogic: BundleProcessingLogic.Sequential, // Set to sequential because this is not running in the context of a parallel-bundle.
                bundleOrchestratorOperation: null, // Set to null because this is not running in the context of a parallel-bundle.
                requestContext: _originalFhirRequestContext,
                auditEventTypeMapping: _auditEventTypeMapping,
                requestContextAccessor: _fhirRequestContextAccessor,
                bundleHttpContextAccessor: _bundleHttpContextAccessor,
                logger: _logger);
        }

        /// <summary>
        /// This method setups new FHIR Request Contexts for downstream requests created by during the bundle processing.
        /// In this method, data in memory from HttpContext, RouteContext, RequestContext and RequestContextAcessor are set to be reused
        /// by the nested requests.
        ///
        /// Static implementation of 'SetupContexts'. Originally created to support parallel operations and avoid the manipulation of local
        /// attributes, that would cause non-thread safe issues.
        /// </summary>
        private static void SetupContexts(
            BundleType? bundleType,
            RouteContext request,
            HTTPVerb httpVerb,
            string persistedId,
            HttpContext httpContext,
            BundleProcessingLogic processingLogic,
            IBundleOrchestratorOperation bundleOrchestratorOperation,
            IFhirRequestContext requestContext,
            IAuditEventTypeMapping auditEventTypeMapping,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            ILogger<BundleHandler> logger)
        {
            Guid bundleOperationId = Guid.Empty;

            // Validation to make sure that the Bundle Orchestrator Operation is not null for parallel bundles.
            if (processingLogic == BundleProcessingLogic.Parallel)
            {
                if (bundleOrchestratorOperation == null)
                {
                    throw new InvalidOperationException("Bundle Orchestrator Operation should not be null for parallel-bundles.");
                }

                bundleOperationId = bundleOrchestratorOperation.Id;
            }

            request.RouteData.Values.TryGetValue("controller", out object controllerName);
            request.RouteData.Values.TryGetValue("action", out object actionName);
            request.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);

            var newFhirRequestContext = new FhirRequestContext(
                httpContext.Request.Method,
                httpContext.Request.GetDisplayUrl(),
                requestContext.BaseUri.OriginalString,
                requestContext.CorrelationId,
                requestHeaders: httpContext.Request.Headers,
                responseHeaders: httpContext.Response.Headers)
            {
                Principal = requestContext.Principal,
                ResourceType = resourceType?.ToString(),
                AuditEventType = auditEventTypeMapping.GetAuditEventType(controllerName?.ToString(), actionName?.ToString()),
                ExecutingBatchOrTransaction = true,
                AccessControlContext = requestContext.AccessControlContext.Clone() as AccessControlContext,
            };

            // Copy allowed resource actions to the new FHIR Request Context.
            foreach (var scopeRestriction in requestContext.AccessControlContext.AllowedResourceActions)
            {
                newFhirRequestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            // Copy allowed properties from the existing FHIR Request Context property bag to the new FHIR Request Context.
            if (requestContext.Properties.Any())
            {
                foreach (string propertyName in PropertiesToAccumulate)
                {
                    if (requestContext.Properties.TryGetValue(propertyName, out object value))
                    {
                        newFhirRequestContext.Properties.Add(propertyName, value);
                    }
                }
            }

            // Propagate Fine Grained Access Control to the new FHIR Request Context.
            newFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = requestContext.AccessControlContext.ApplyFineGrainedAccessControl;
            newFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControlWithSearchParameters = requestContext.AccessControlContext.ApplyFineGrainedAccessControlWithSearchParameters;

            // Propagate bundle context information to inner requests.
            BundleResourceContext bundleResourceExecutionContext = new BundleResourceContext(
                bundleType,
                processingLogic,
                httpVerb,
                persistedId: persistedId,
                bundleOperationId: bundleOperationId);
            newFhirRequestContext.RequestHeaders.Add(BundleOrchestratorNamingConventions.HttpBundleInnerRequestExecutionContext, JObject.FromObject(bundleResourceExecutionContext).ToString());

            requestContextAccessor.RequestContext = newFhirRequestContext;
            bundleHttpContextAccessor.HttpContext = httpContext;

            // Copy allowed headers from the FHIR Request Context response header.
            if (requestContext.ResponseHeaders.Any())
            {
                foreach (string headerName in HeadersToAccumulate)
                {
                    if (requestContext.ResponseHeaders.TryGetValue(headerName, out var values))
                    {
                        newFhirRequestContext.ResponseHeaders.Add(headerName, values);
                    }
                }
            }
        }

        private void PopulateReferenceIdDictionary(IReadOnlyCollection<EntryComponent> bundleEntries, IDictionary<string, (string resourceId, string resourceType)> idDictionary)
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

                            _totalGeneratedIdentifiers++;
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// Given a resource type, detect if it should trigger a profile refresh.
        /// </summary>
        private void DetectNeedToRefreshProfiles(string resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return;
            }

            if (_profilesResolver.GetProfilesTypes().Contains(resourceType))
            {
                _forceProfilesRefresh = true;
            }
        }

        /// <summary>
        /// If the profile refresh is needed, then execute it.
        /// </summary>
        private void RefreshProfilesIfApplicable()
        {
            if (_forceProfilesRefresh)
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    _profilesResolver.Refresh();
                    _logger.LogInformation("FHIR Profiles cache is refreshed. Elapsed time: {ElapsedMilliseconds}", watch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FHIR Profiles cache failed while refreshing. Elapsed time: {ElapsedMilliseconds}", watch.ElapsedMilliseconds);
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

        private static string SanitizeString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            return input
                .Replace(Environment.NewLine, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\r", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("\n", " ", StringComparison.OrdinalIgnoreCase);
        }

        private BundleHandlerStatistics CreateNewBundleHandlerStatistics(BundleProcessingLogic processingLogic)
        {
            BundleHandlerStatistics statistics = new BundleHandlerStatistics(
                _bundleType,
                processingLogic,
                _optimizedQuerySet,
                _requestCount,
                _totalGeneratedIdentifiers,
                _totalResolvedReferences);

            statistics.StartCollectingResults();

            return statistics;
        }

        private void FinishCollectingBundleStatistics(BundleHandlerStatistics statistics)
        {
            statistics.StopCollectingResults();

            try
            {
                string statisticsAsJson = statistics.GetStatisticsAsJson();
                _logger.LogInformation(statisticsAsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error computing bundle statistics. This error will not block the bundle processing.");
            }
        }
    }
}
