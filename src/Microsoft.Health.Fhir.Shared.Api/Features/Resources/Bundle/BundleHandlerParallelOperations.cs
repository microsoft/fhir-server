// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Set of operations to handle a bundle in multiple parallel operations.
    /// </summary>
    public partial class BundleHandler
    {
        private readonly string[] _bundleExpectedStatusCodes = new string[]
        {
            ((int)HttpStatusCode.OK).ToString(),
            ((int)HttpStatusCode.Created).ToString(),
            ((int)HttpStatusCode.NoContent).ToString(),
            ((int)HttpStatusCode.BadRequest).ToString(), // Duplicate
            ((int)HttpStatusCode.NotFound).ToString(),
        };

        private readonly HTTPVerb[] _readOnlyHttpVerbs = new HTTPVerb[]
        {
            HTTPVerb.GET,
#if !STU3
            HTTPVerb.HEAD,
#endif
        };

        private async Task<EntryComponent> ExecuteRequestsInParallelAsync(
            Hl7.Fhir.Model.Bundle responseBundle,
            List<ResourceExecutionContext> resources,
            IBundleOrchestratorOperation bundleOperation,
            EntryComponent throttledEntryComponent,
            BundleHandlerStatistics statistics,
            CancellationToken cancellationToken)
        {
            if (!resources.Any())
            {
                return await Task.FromResult(throttledEntryComponent);
            }

            const int GCCollectTrigger = 150;

            IAuditEventTypeMapping auditEventTypeMapping = _auditEventTypeMapping;
            RequestContextAccessor<IFhirRequestContext> requestContext = _fhirRequestContextAccessor;
            FhirJsonParser fhirJsonParser = _fhirJsonParser;
            IBundleHttpContextAccessor bundleHttpContextAccessor = _bundleHttpContextAccessor;

            // Parallel Resource Handling Function.
            Func<ResourceExecutionContext, CancellationToken, Task> handleRequestFunction = async (ResourceExecutionContext resourceExecutionContext, CancellationToken ct) =>
            {
                if (resourceExecutionContext.Index > 0 && resourceExecutionContext.Index % GCCollectTrigger == 0)
                {
                    RunGarbageCollection();
                }

                _logger.LogTrace("BundleHandler - Running '{HttpVerb}' Request #{RequestNumber} out of {TotalNumberOfRequests}.", resourceExecutionContext.HttpVerb, resourceExecutionContext.Index, bundleOperation.OriginalExpectedNumberOfResources);

                // Creating new instances per record in the bundle, and making their access thread-safe.
                // Avoiding possible internal conflicts due the parallel access from multiple threads.
                ResourceIdProvider resourceIdProvider = new ResourceIdProvider();
                IFhirRequestContext originalFhirRequestContext = (IFhirRequestContext)_originalFhirRequestContext.Clone();

                Stopwatch watch = Stopwatch.StartNew();

                EntryComponent entry = await HandleRequestAsync(
                    responseBundle,
                    resourceExecutionContext.HttpVerb,
                    throttledEntryComponent,
                    _bundleType,
                    bundleOperation,
                    resourceExecutionContext.Context,
                    resourceExecutionContext.Index,
                    resourceExecutionContext.PersistedId,
                    _requestCount,
                    auditEventTypeMapping,
                    originalFhirRequestContext,
                    requestContext,
                    bundleHttpContextAccessor,
                    resourceIdProvider,
                    fhirJsonParser,
                    _logger,
                    ct);

                statistics.RegisterNewEntry(resourceExecutionContext.HttpVerb, resourceExecutionContext.Index, entry.Response.Status, watch.Elapsed);

                await SetResourceProcessingStatusAsync(resourceExecutionContext.HttpVerb, resourceExecutionContext, bundleOperation, entry, cancellationToken);

                watch.Stop();
                _logger.LogTrace("BundleHandler - '{HttpVerb}' Request #{RequestNumber} completed with status code '{StatusCode}' in {TotalElapsedMilliseconds}ms.", resourceExecutionContext.HttpVerb, resourceExecutionContext.Index, entry.Response.Status, watch.ElapsedMilliseconds);
            };

            List<Task> requestsPerResource = new List<Task>();
            foreach (ResourceExecutionContext resourceContext in resources)
            {
                requestsPerResource.Add(handleRequestFunction(resourceContext, cancellationToken));
            }

            Task.WaitAll(requestsPerResource.ToArray(), cancellationToken);

            _bundleOrchestrator.CompleteOperation(bundleOperation);

            return throttledEntryComponent;
        }

        /// <summary>
        /// Sets the resource processing status in the bundle operation.
        /// </summary>
        /// <remarks>
        /// Few cases where a resource must be released from a Bundle Orchestrator Operation:
        /// * In the case a resource fails during processing, and the bundle operation is still waiting for it to be merge, that resource should be released from
        /// the bundle operation to avoid the operation getting stuck waiting.
        /// * Read-only operations GET and HEAD follow a different logic than PUT, POST, DELETE and PATCH, and they are not executed as single operation.
        /// * If a resource finishes while the Bundle Operation is still "waiting for resources", it means that resource followed an unexpected path, and
        /// it should be released.
        /// </remarks>
        private async Task SetResourceProcessingStatusAsync(HTTPVerb httpVerb, ResourceExecutionContext resourceExecutionContext, IBundleOrchestratorOperation bundleOperation, EntryComponent entry, CancellationToken cancellationToken)
        {
            string resourceFinalStatusCode = entry.Response.Status;

            if (bundleOperation.Status != BundleOrchestratorOperationStatus.Completed)
            {
                if (_readOnlyHttpVerbs.Contains(httpVerb))
                {
                    _logger.LogTrace(
                        "BundleHandler - Releasing resource #{RequestNumber} as it's a read-only operation. HTTP Verb {HTTPVerb}. HTTP Status Code {HTTPStatusCode}.",
                        resourceExecutionContext.Index,
                        httpVerb,
                        resourceFinalStatusCode);

                    await bundleOperation.ReleaseResourceAsync(
                        $"Resource #{resourceExecutionContext.Index} released as it's readonly request ({httpVerb}). Request completed with HTTP Status Code {resourceFinalStatusCode}.",
                        cancellationToken);
                }
                else if (!_bundleExpectedStatusCodes.Contains(resourceFinalStatusCode))
                {
                    _logger.LogTrace(
                        "BundleHandler - Releasing resource #{RequestNumber} as it has completed with HTTP Status Code {HTTPStatusCode}.",
                        resourceExecutionContext.Index,
                        resourceFinalStatusCode);

                    await bundleOperation.ReleaseResourceAsync(
                        $"Resource #{resourceExecutionContext.Index} completed with HTTP Status Code {resourceFinalStatusCode}.",
                        cancellationToken);
                }
                else if (bundleOperation.Status == BundleOrchestratorOperationStatus.WaitingForResources)
                {
                    _logger.LogTrace(
                        "BundleHandler - Releasing resource #{RequestNumber} as it has completed while Bundle Operation is waiting for resources. HTTP Status Code {HTTPStatusCode}.",
                        resourceExecutionContext.Index,
                        resourceFinalStatusCode);

                    await bundleOperation.ReleaseResourceAsync(
                        $"Resource #{resourceExecutionContext.Index} as it has completed while Bundle Operation is waiting for resources. HTTP Status Code {resourceFinalStatusCode}.",
                        cancellationToken);
                }
            }
        }

        private static async Task<EntryComponent> HandleRequestAsync(
            Hl7.Fhir.Model.Bundle responseBundle,
            HTTPVerb httpVerb,
            EntryComponent throttledEntryComponent,
            BundleType? bundleType,
            IBundleOrchestratorOperation bundleOperation,
            RouteContext request,
            int entryIndex,
            string persistedId,
            int requestCount,
            IAuditEventTypeMapping auditEventTypeMapping,
            IFhirRequestContext originalFhirRequestContext,
            RequestContextAccessor<IFhirRequestContext> requestContext,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            ResourceIdProvider resourceIdProvider,
            FhirJsonParser fhirJsonParser,
            ILogger<BundleHandler> logger,
            CancellationToken cancellationToken)
        {
            EntryComponent entryComponent;

            if (request.Handler != null)
            {
                if (throttledEntryComponent != null)
                {
                    // A previous action was throttled.
                    // Skip executing subsequent actions and include the 429 response.
                    logger.LogTrace("BundleHandler was throttled, subsequent actions will be skipped and HTTP 429 will be added as a result. HttpVerb:{HttpVerb} BundleSize: {RequestCount} EntryIndex: {EntryIndex}", httpVerb, requestCount, entryIndex);
                    entryComponent = throttledEntryComponent;
                }
                else
                {
                    HttpContext httpContext = request.HttpContext;
                    Func<string> originalResourceIdProvider = resourceIdProvider.Create;
                    if (!string.IsNullOrWhiteSpace(persistedId))
                    {
                        resourceIdProvider.Create = () => persistedId;
                    }

                    SetupContexts(request, httpVerb, httpContext, bundleOperation, originalFhirRequestContext, auditEventTypeMapping, requestContext, bundleHttpContextAccessor);

                    // Attempt 1.
                    await request.Handler.Invoke(httpContext);

                    // Should we continue retrying HTTP 429s?
                    // As we'll start running more requests in parallel, the risk of raising more HTTP 429s is hight.
                    // -----------
                    // We will retry a 429 one time per request in the bundle
                    if (httpContext.Response.StatusCode == (int)HttpStatusCode.TooManyRequests)
                    {
                        logger.LogTrace("BundleHandler received HTTP 429 response, attempting retry.  HttpVerb:{HttpVerb} BundleSize:{RequestCount} EntryIndex:{EntryIndex}", httpVerb, requestCount, entryIndex);
                        int retryDelay = 2;
                        var retryAfterValues = httpContext.Response.Headers.GetCommaSeparatedValues("Retry-After");
                        if (retryAfterValues != StringValues.Empty && int.TryParse(retryAfterValues[0], out var retryHeaderValue))
                        {
                            retryDelay = retryHeaderValue;
                        }

                        await Task.Delay(retryDelay * 1000, cancellationToken); // multiply by 1000 as retry-header specifies delay in seconds

                        // Attempt 2.
                        await request.Handler.Invoke(httpContext);
                    }

                    resourceIdProvider.Create = originalResourceIdProvider;

                    entryComponent = CreateEntryComponent(fhirJsonParser, httpContext);

                    foreach (string headerName in HeadersToAccumulate)
                    {
                        if (httpContext.Response.Headers.TryGetValue(headerName, out var values))
                        {
                            originalFhirRequestContext.ResponseHeaders[headerName] = values;
                        }
                    }

                    if (bundleType == BundleType.Batch && entryComponent.Response.Status == "429")
                    {
                        logger.LogTrace("BundleHandler received HTTP 429 response after retry, now aborting remainder of bundle. HttpVerb:{HttpVerb} BundleSize:{RequestCount} EntryIndex:{EntryIndex}", httpVerb, requestCount, entryIndex);

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
                            string.Format(Api.Resources.BundleNotFound, $"{request.HttpContext.Request.Path}{request.HttpContext.Request.QueryString}")),
                    },
                };
            }

            if (bundleType.Equals(BundleType.Transaction) && entryComponent.Response.Outcome != null)
            {
                var errorMessage = string.Format(Api.Resources.TransactionFailed, request.HttpContext.Request.Method, request.HttpContext.Request.Path);

                if (!Enum.TryParse(entryComponent.Response.Status, out HttpStatusCode httpStatusCode))
                {
                    httpStatusCode = HttpStatusCode.BadRequest;
                }

                TransactionExceptionHandler.ThrowTransactionException(errorMessage, httpStatusCode, (OperationOutcome)entryComponent.Response.Outcome);
            }

            responseBundle.Entry[entryIndex] = entryComponent;

            return entryComponent;
        }

        private static void SetupContexts(
            RouteContext request,
            HTTPVerb httpVerb,
            HttpContext httpContext,
            IBundleOrchestratorOperation bundleOperation,
            IFhirRequestContext requestContext,
            IAuditEventTypeMapping auditEventTypeMapping,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IBundleHttpContextAccessor bundleHttpContextAccessor)
        {
            request.RouteData.Values.TryGetValue("controller", out object controllerName);
            request.RouteData.Values.TryGetValue("action", out object actionName);
            request.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);

            var newFhirRequestContext = new FhirRequestContext(
                httpContext.Request.Method,
                httpContext.Request.GetDisplayUrl(),
                requestContext.BaseUri.OriginalString,
                requestContext.CorrelationId,
                httpContext.Request.Headers,
                httpContext.Response.Headers)
            {
                Principal = requestContext.Principal,
                ResourceType = resourceType?.ToString(),
                AuditEventType = auditEventTypeMapping.GetAuditEventType(
                    controllerName?.ToString(),
                    actionName?.ToString()),
                ExecutingBatchOrTransaction = true,
            };

            // Assign the current Bundle Orchestrator Operation ID as part of the downstream requests.
            newFhirRequestContext.RequestHeaders.Add(BundleOrchestratorNamingConventions.HttpHeaderOperationTag, bundleOperation.Id.ToString());

            // Assign the current Bundle Orchestrator Operation ID as part of the downstream requests.
            newFhirRequestContext.RequestHeaders.Add(BundleOrchestratorNamingConventions.HttpHeaderBundleResourceHttpVerb, httpVerb.ToString());

            requestContextAccessor.RequestContext = newFhirRequestContext;

            bundleHttpContextAccessor.HttpContext = httpContext;

            foreach (string headerName in HeadersToAccumulate)
            {
                if (requestContext.ResponseHeaders.TryGetValue(headerName, out var values))
                {
                    newFhirRequestContext.ResponseHeaders.Add(headerName, values);
                }
            }
        }

        private struct ResourceExecutionContext
        {
            public ResourceExecutionContext(HTTPVerb httpVerb, RouteContext context, int index, string persistedId)
            {
                HttpVerb = httpVerb;
                Context = context;
                Index = index;
                PersistedId = persistedId;
            }

            public HTTPVerb HttpVerb { get; private set; }

            public RouteContext Context { get; private set; }

            public int Index { get; private set; }

            public string PersistedId { get; private set; }
        }
    }
}
