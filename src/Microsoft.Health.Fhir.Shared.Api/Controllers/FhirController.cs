﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Api.Features.AnonymousOperation;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionConstraints;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.AnonymousOperations;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// FHIR Rest API
    /// </summary>
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ServiceFilter(typeof(QueryLatencyOverEfficiencyFilterAttribute))]
    [ServiceFilter(typeof(QueryCacheFilterAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    public class FhirController : Controller
    {
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirController" /> class.
        /// </summary>
        /// <param name="mediator">The mediator.</param>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="urlResolver">The urlResolver.</param>
        /// <param name="uiConfiguration">The UI configuration.</param>
        /// <param name="authorizationService">The authorization service.</param>
        public FhirController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<FeatureConfiguration> uiConfiguration,
            IAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(uiConfiguration, nameof(uiConfiguration));
            EnsureArg.IsNotNull(uiConfiguration.Value, nameof(uiConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route(KnownRoutes.CustomError)]
        [AllowAnonymous]
        public IActionResult CustomError(int? statusCode = null)
        {
            HttpStatusCode returnCode;
            OperationOutcome.IssueType issueType;
            string diagnosticInfo;

            switch (statusCode)
            {
                case (int)HttpStatusCode.Unauthorized:
                    issueType = OperationOutcome.IssueType.Login;
                    returnCode = HttpStatusCode.Unauthorized;
                    diagnosticInfo = Resources.Unauthorized;
                    break;
                case (int)HttpStatusCode.Forbidden:
                    issueType = OperationOutcome.IssueType.Forbidden;
                    returnCode = HttpStatusCode.Forbidden;
                    diagnosticInfo = Resources.Forbidden;
                    break;
                case (int)HttpStatusCode.NotFound:
                    issueType = OperationOutcome.IssueType.NotFound;
                    returnCode = HttpStatusCode.NotFound;
                    diagnosticInfo = Resources.NotFoundException;
                    break;
                case (int)HttpStatusCode.MethodNotAllowed:
                    issueType = OperationOutcome.IssueType.NotSupported;
                    returnCode = HttpStatusCode.MethodNotAllowed;
                    diagnosticInfo = Resources.OperationNotSupported;
                    break;
                default:
                    issueType = OperationOutcome.IssueType.Exception;
                    returnCode = HttpStatusCode.InternalServerError;
                    diagnosticInfo = Resources.GeneralInternalError;
                    break;
            }

            return FhirResult.Create(
                new OperationOutcome
                {
                    Id = _fhirRequestContextAccessor.RequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new OperationOutcome.IssueComponent
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = issueType,
                            Diagnostics = diagnosticInfo,
                        },
                    },
                }.ToResourceElement(),
                returnCode);
        }

        /// <summary>
        /// Creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        [HttpPost]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.Create)]
        [ServiceFilter(typeof(SearchParameterFilterAttribute))]
        [TypeFilter(typeof(CrudEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> Create([FromBody] Resource resource)
        {
            RawResourceElement response = await _mediator.CreateResourceAsync(
                new CreateResourceRequest(resource.ToResourceElement(), GetBundleResourceContext()),
                HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.Created)
                .SetETagHeader()
                .SetLastModifiedHeader()
                .SetLocationHeader(_urlResolver);
        }

        /// <summary>
        /// Conditionally creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        [HttpPost]
        [ConditionalConstraint]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.ConditionalCreate)]
        public async Task<IActionResult> ConditionalCreate([FromBody] Resource resource)
        {
            StringValues conditionalCreateHeader = HttpContext.Request.Headers[KnownHeaders.IfNoneExist];
            var preferHeader = _fhirRequestContextAccessor.GetReturnPreferenceValue();

            SetupConditionalRequestWithQueryOptimizeConcurrency();

            Tuple<string, string>[] conditionalParameters = QueryHelpers.ParseQuery(conditionalCreateHeader)
                .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();

            UpsertResourceResponse createResponse = await _mediator.Send<UpsertResourceResponse>(
                new ConditionalCreateResourceRequest(resource.ToResourceElement(), conditionalParameters, GetBundleResourceContext()),
                HttpContext.RequestAborted);

            if (createResponse?.Outcome == null)
            {
                return Ok();
            }

            var statusCode = HttpStatusCode.Created;
            var message = Resources.ConditionalCreateResourceCreated;
            if (createResponse.Outcome.Outcome != SaveOutcomeType.Created)
            {
                statusCode = HttpStatusCode.OK;
                message = Resources.ConditionalCreateResourceAlreadyExists;
            }

            return FhirResult.Create(
                createResponse.Outcome.RawResourceElement,
                statusCode,
                true,
                true,
                true,
                _urlResolver,
                preferHeader,
                message);
        }

        /// <summary>
        /// Updates or creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="ifMatchHeader">Optional If-Match header</param>
        [HttpPut]
        [ValidateResourceIdFilter]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Update)]
        [TypeFilter(typeof(CrudEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> Update([FromBody] Resource resource, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            SaveOutcome response = await _mediator.UpsertResourceAsync(
                new UpsertResourceRequest(resource.ToResourceElement(), GetBundleResourceContext(), ifMatchHeader),
                HttpContext.RequestAborted);

            return ToSaveOutcomeResult(response);
        }

        /// <summary>
        /// Updates or creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        [HttpPut]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.ConditionalUpdate)]
        public async Task<IActionResult> ConditionalUpdate([FromBody] Resource resource)
        {
            SetupConditionalRequestWithQueryOptimizeConcurrency();

            IReadOnlyList<Tuple<string, string>> conditionalParameters = GetQueriesForSearch();

            UpsertResourceResponse response = await _mediator.Send<UpsertResourceResponse>(
                new ConditionalUpsertResourceRequest(resource.ToResourceElement(), conditionalParameters, GetBundleResourceContext()),
                HttpContext.RequestAborted);

            SaveOutcome saveOutcome = response.Outcome;

            return ToSaveOutcomeResult(saveOutcome);
        }

        private FhirResult ToSaveOutcomeResult(SaveOutcome saveOutcome)
        {
            switch (saveOutcome.Outcome)
            {
                case SaveOutcomeType.Created:
                    return FhirResult.Create(saveOutcome.RawResourceElement, HttpStatusCode.Created)
                        .SetETagHeader()
                        .SetLastModifiedHeader()
                        .SetLocationHeader(_urlResolver);
                case SaveOutcomeType.Updated:
                    return FhirResult.Create(saveOutcome.RawResourceElement, HttpStatusCode.OK)
                        .SetETagHeader()
                        .SetLastModifiedHeader();
            }

            return FhirResult.Create(saveOutcome.RawResourceElement, HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// Reads the specified resource.
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        [HttpGet]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.ResourceTypeById, Name = RouteNames.ReadResource)]
        [AuditEventType(AuditEventSubType.Read)]
        [TypeFilter(typeof(CrudEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> Read(string typeParameter, string idParameter)
        {
            RawResourceElement response = await _mediator.GetResourceAsync(
                new GetResourceRequest(new ResourceKey(typeParameter, idParameter), GetBundleResourceContext()),
                HttpContext.RequestAborted);

            return FhirResult.Create(response)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        /// <summary>
        /// Returns the history of all resources in the system
        /// </summary>
        /// <param name="historyModel">Model for history parameters.</param>
        [HttpGet]
        [Route(KnownRoutes.History, Name = RouteNames.History)]
        [AuditEventType(AuditEventSubType.HistorySystem)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> SystemHistory(HistoryModel historyModel)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(
                historyModel.Since,
                historyModel.Before,
                historyModel.At,
                historyModel.Count,
                historyModel.Summary,
                historyModel.ContinuationToken,
                historyModel.Sort,
                HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the history of a specific resource type
        /// </summary>
        /// <param name="typeParameter">The resource type.</param>
        /// <param name="historyModel">Model for history parameters.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceTypeHistory, Name = RouteNames.HistoryType)]
        [AuditEventType(AuditEventSubType.HistoryType)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> TypeHistory(
            string typeParameter,
            HistoryModel historyModel)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(
                typeParameter,
                historyModel.Since,
                historyModel.Before,
                historyModel.At,
                historyModel.Count,
                historyModel.Summary,
                historyModel.ContinuationToken,
                historyModel.Sort,
                HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the history of a resource
        /// </summary>
        /// <param name="typeParameter">The resource type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="historyModel">Model for history parameters.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceTypeByIdHistory, Name = RouteNames.HistoryTypeId)]
        [AuditEventType(AuditEventSubType.HistoryInstance)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> History(
            string typeParameter,
            string idParameter,
            HistoryModel historyModel)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(
                typeParameter,
                idParameter,
                historyModel.Since,
                historyModel.Before,
                historyModel.At,
                historyModel.Count,
                historyModel.Summary,
                historyModel.ContinuationToken,
                historyModel.Sort,
                HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Reads the specified version of the resource.
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="vidParameter">The versionId.</param>
        [HttpGet]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.ResourceTypeByIdAndVid, Name = RouteNames.ReadResourceWithVersionRoute)]
        [AuditEventType(AuditEventSubType.VRead)]
        [TypeFilter(typeof(CrudEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> VRead(string typeParameter, string idParameter, string vidParameter)
        {
            RawResourceElement response = await _mediator.GetResourceAsync(
                new GetResourceRequest(new ResourceKey(typeParameter, idParameter, vidParameter), GetBundleResourceContext()),
                HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.OK)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        /// <summary>
        /// Deletes the specified resource
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="hardDeleteModel">The model for hard-delete indicating whether to hard-delete the resource or not.</param>
        /// <param name="allowPartialSuccess">Allows for partial success of delete operation. Only applicable for hard delete on Cosmos services</param>
        [HttpDelete]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Delete)]
        [TypeFilter(typeof(CrudEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> Delete(string typeParameter, string idParameter, HardDeleteModel hardDeleteModel, [FromQuery] bool allowPartialSuccess)
        {
            DeleteResourceResponse response = await _mediator.DeleteResourceAsync(
                new DeleteResourceRequest(
                    new ResourceKey(typeParameter, idParameter),
                    hardDeleteModel.IsHardDelete ? DeleteOperation.HardDelete : DeleteOperation.SoftDelete,
                    GetBundleResourceContext(),
                    allowPartialSuccess),
                HttpContext.RequestAborted);

            return FhirResult.NoContent().SetETagHeader(response.WeakETag);
        }

        /// <summary>
        /// Deletes the specified resource's history, keeping the current version
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="allowPartialSuccess">Allows for partial success of delete operation. Only applicable on Cosmos services</param>
        [HttpDelete]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.PurgeHistoryResourceTypeById)]
        [AuditEventType(AuditEventSubType.PurgeHistory)]
        public async Task<IActionResult> PurgeHistory(string typeParameter, string idParameter, [FromQuery] bool allowPartialSuccess)
        {
            DeleteResourceResponse response = await _mediator.DeleteResourceAsync(
                new DeleteResourceRequest(
                    new ResourceKey(typeParameter, idParameter),
                    DeleteOperation.PurgeHistory,
                    GetBundleResourceContext(),
                    allowPartialSuccess),
                HttpContext.RequestAborted);

            return FhirResult.NoContent().SetETagHeader(response.WeakETag);
        }

        /// <summary>
        /// Deletes the specified resource
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="hardDeleteModel">The model for hard-delete indicating whether to hard-delete the resource or not.</param>
        /// <param name="maxDeleteCount">Specifies the maximum number of resources that can be deleted.</param>
        [HttpDelete]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.ConditionalDelete)]
        public async Task<IActionResult> ConditionalDelete(string typeParameter, HardDeleteModel hardDeleteModel, [FromQuery(Name = KnownQueryParameterNames.Count)] int? maxDeleteCount)
        {
            IReadOnlyList<Tuple<string, string>> conditionalParameters = GetQueriesForSearch();

            SetupConditionalRequestWithQueryOptimizeConcurrency();

            DeleteResourceResponse response = await _mediator.Send(
                new ConditionalDeleteResourceRequest(
                    typeParameter,
                    conditionalParameters,
                    hardDeleteModel.IsHardDelete ? DeleteOperation.HardDelete : DeleteOperation.SoftDelete,
                    maxDeleteCount.GetValueOrDefault(1),
                    GetBundleResourceContext()),
                HttpContext.RequestAborted);

            if (maxDeleteCount.HasValue)
            {
                Response.Headers[KnownHeaders.ItemsDeleted] = (response?.ResourcesDeleted ?? 0).ToString(CultureInfo.InvariantCulture);
            }

            return FhirResult.NoContent().SetETagHeader(response?.WeakETag);
        }

        /// <summary>
        /// Patches the specified resource.
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="patchDocument">The JSON patch document.</param>
        /// <param name="ifMatchHeader">Optional If-Match header.</param>
        [HttpPatch]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Patch)]
        [Consumes("application/json-patch+json")]
        public async Task<IActionResult> PatchJson(string typeParameter, string idParameter, [FromBody] JsonPatchDocument patchDocument, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            var payload = new JsonPatchPayload(patchDocument);

            UpsertResourceResponse response = await _mediator.PatchResourceAsync(
                new PatchResourceRequest(
                    new ResourceKey(typeParameter, idParameter),
                    payload,
                    GetBundleResourceContext(),
                    ifMatchHeader),
                HttpContext.RequestAborted);

            return ToSaveOutcomeResult(response.Outcome);
        }

        /// <summary>
        /// Conditionally patches a specified resource.
        /// </summary>
        /// <param name="typeParameter">Type of resource to patch.</param>
        /// <param name="patchDocument">The JSON patch document.</param>
         /// <param name="ifMatchHeader">Optional If-Match header.</param>
        [HttpPatch]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.ConditionalPatch)]
        [Consumes("application/json-patch+json")]
        public async Task<IActionResult> ConditionalPatchJson(string typeParameter, [FromBody] JsonPatchDocument patchDocument, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            IReadOnlyList<Tuple<string, string>> conditionalParameters = GetQueriesForSearch();
            var payload = new JsonPatchPayload(patchDocument);

            SetupConditionalRequestWithQueryOptimizeConcurrency();

            UpsertResourceResponse response = await _mediator.ConditionalPatchResourceAsync(
                new ConditionalPatchResourceRequest(typeParameter, payload, conditionalParameters, GetBundleResourceContext(), ifMatchHeader),
                HttpContext.RequestAborted);
            return ToSaveOutcomeResult(response.Outcome);
        }

        /// <summary>
        /// Patches the specified resource.
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="paramsResource">The JSON FHIR Parameters Resource.</param>
        /// <param name="ifMatchHeader">Optional If-Match header.</param>
        [HttpPatch]
        [ValidateIdSegmentAttribute]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Patch)]
        [Consumes("application/fhir+json")]
        public async Task<IActionResult> PatchFhir(string typeParameter, string idParameter, [FromBody] Parameters paramsResource, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            var payload = new FhirPathPatchPayload(paramsResource);

            UpsertResourceResponse response = await _mediator.PatchResourceAsync(
                new PatchResourceRequest(new ResourceKey(typeParameter, idParameter), payload, GetBundleResourceContext(), ifMatchHeader),
                HttpContext.RequestAborted);
            return ToSaveOutcomeResult(response.Outcome);
        }

        /// <summary>
        /// Conditionally patches a specified resource.
        /// </summary>
        /// <param name="typeParameter">Type of resource to patch.</param>
        /// <param name="paramsResource">The JSON FHIR Parameters Resource.</param>
        /// <param name="ifMatchHeader">Optional If-Match header.</param>
        [HttpPatch]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.ConditionalPatch)]
        [Consumes("application/fhir+json")]
        public async Task<IActionResult> ConditionalPatchFhir(string typeParameter, [FromBody] Parameters paramsResource, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            IReadOnlyList<Tuple<string, string>> conditionalParameters = GetQueriesForSearch();
            var payload = new FhirPathPatchPayload(paramsResource);

            SetupConditionalRequestWithQueryOptimizeConcurrency();

            UpsertResourceResponse response = await _mediator.ConditionalPatchResourceAsync(
                new ConditionalPatchResourceRequest(typeParameter, payload, conditionalParameters, GetBundleResourceContext(), ifMatchHeader),
                HttpContext.RequestAborted);
            return ToSaveOutcomeResult(response.Outcome);
        }

        /// <summary>
        /// Searches across all resource types.
        /// </summary>
        [HttpGet]
        [Route("", Name = RouteNames.SearchAllResources)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> Search()
        {
            return await SearchByResourceType(typeParameter: null);
        }

        /// <summary>
        /// Searches for resources.
        /// </summary>
        /// <param name="typeParameter">The resource type.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceType, Name = RouteNames.SearchResources)]
        [AuditEventType(AuditEventSubType.SearchType)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> SearchByResourceType(string typeParameter)
        {
            return await PerformSearch(typeParameter, GetQueriesForSearch());
        }

        private IReadOnlyList<Tuple<string, string>> GetQueriesForSearch()
        {
            return Request.GetQueriesForSearch();
        }

        /// <summary>
        /// Searches by compartment.
        /// </summary>
        /// <param name="compartmentTypeParameter">The compartment type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="typeParameter">The resource type.</param>
        [HttpGet]
        [Route(KnownRoutes.CompartmentTypeByResourceType, Name = RouteNames.SearchCompartmentByResourceType)]
        [AuditEventType(AuditEventSubType.Search)]
        [TypeFilter(typeof(SearchEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> SearchCompartmentByResourceType(string compartmentTypeParameter, string idParameter, string typeParameter)
        {
            IReadOnlyList<Tuple<string, string>> queries = GetQueriesForSearch();
            return await PerformCompartmentSearch(compartmentTypeParameter, idParameter, typeParameter, queries);
        }

        private async Task<IActionResult> PerformCompartmentSearch(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queries)
        {
            ResourceElement response = await _mediator.SearchResourceCompartmentAsync(compartmentType, compartmentId, resourceType, queries, HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        private async Task<IActionResult> PerformSearch(string type, IReadOnlyList<Tuple<string, string>> queries)
        {
            ResourceElement response = await _mediator.SearchResourceAsync(type, queries, HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the Capability Statement of this server which is used to determine
        /// what FHIR features are supported by this implementation.
        /// </summary>
        [HttpGet]
        [FhirAnonymousOperation(FhirAnonymousOperationType.Metadata)]
        [Route(KnownRoutes.Metadata, Name = RouteNames.Metadata)]
        public async Task<IActionResult> Metadata()
        {
            ResourceElement response = await _mediator.GetCapabilitiesAsync(HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the SMART configuration of this server.
        /// </summary>
        [HttpGet]
        [FhirAnonymousOperation(FhirAnonymousOperationType.WellKnown)]
        [Route(KnownRoutes.WellKnownSmartConfiguration, Name = RouteNames.WellKnownSmartConfiguration)]
        public async Task<IActionResult> WellKnownSmartConfiguration()
        {
            SmartConfigurationResult response = await _mediator.GetSmartConfigurationAsync(HttpContext.RequestAborted);

            return OperationSmartConfigurationResult.Ok(response);
        }

        /// <summary>
        /// Returns the list of versions the server supports along with the default version it will use if no fhirVersion parameter is present.
        /// </summary>
        [HttpGet]
        [FhirAnonymousOperation(FhirAnonymousOperationType.Versions)]
        [Route(KnownRoutes.Versions)]
        public async Task<IActionResult> Versions()
        {
            VersionsResult response = await _mediator.GetOperationVersionsAsync(HttpContext.RequestAborted);

            return new OperationVersionsResult(response, HttpStatusCode.OK);
        }

        /// <summary>
        /// Handles batch and transaction requests
        /// </summary>
        /// <param name="bundle">The bundle being posted</param>
        [HttpPost]
        [Route("", Name = RouteNames.PostBundle)]
        [AuditEventType(AuditEventSubType.BundlePost)]
        [TypeFilter(typeof(BundleEndpointMetricEmitterAttribute))]
        public async Task<IActionResult> BatchAndTransactions([FromBody] Resource bundle)
        {
            ResourceElement bundleResponse = await _mediator.PostBundle(bundle.ToResourceElement(), HttpContext.RequestAborted);

            return FhirResult.Create(bundleResponse);
        }

        /// <summary>
        /// Returns an instance of <see cref="BundleResourceContext"/> with bundle related information, if a resource if part of a bundle.
        /// </summary>
        /// <returns>Returns null if the resource is not part of a bundle.</returns>
        private BundleResourceContext GetBundleResourceContext()
        {
            if (HttpContext?.Request?.Headers != null)
            {
                // Step 1 - Retrieve Bundle Processing Logic.
                if (HttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpInnerBundleRequestProcessingLogic, out StringValues rawBundleProcessingLogic))
                {
                    if (Enum.TryParse<BundleProcessingLogic>(rawBundleProcessingLogic, out BundleProcessingLogic bundleProcessingLogic))
                    {
                        // Step 2 - Retrieve Bundle Operation ID.
                        if (HttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpInnerBundleRequestHeaderOperationTag, out StringValues responseOperationId))
                        {
                            string rawId = responseOperationId.FirstOrDefault();
                            if (Guid.TryParse(rawId, out Guid bundleOperationId))
                            {
                                // Step 3 - Retrieve resource HTTP verb.
                                if (HttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpInnerBundleRequestHeaderBundleResourceHttpVerb, out StringValues responseHttpVerb))
                                {
                                    string rawHttpVerb = responseHttpVerb.FirstOrDefault();
                                    if (Enum.TryParse<HTTPVerb>(rawHttpVerb, ignoreCase: true, out HTTPVerb httpVerb))
                                    {
                                        return new BundleResourceContext(bundleProcessingLogic, httpVerb, bundleOperationId);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private void SetupConditionalRequestWithQueryOptimizeConcurrency()
        {
            if (HttpContext?.Request?.Headers != null && _fhirRequestContextAccessor != null)
            {
                ConditionalQueryProcessingLogic processingLogic = HttpContext.GetConditionalQueryProcessingLogic();

                if (processingLogic == ConditionalQueryProcessingLogic.Parallel)
                {
                    _fhirRequestContextAccessor.RequestContext.DecorateRequestContextWithOptimizedConcurrency();
                }
            }
        }
    }
}
