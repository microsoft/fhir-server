// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionConstraints;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
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
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    public class FhirController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ILogger<FhirController> _logger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly IAuthorizationService _authorizationService;
        private readonly FeatureConfiguration _featureConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirController" /> class.
        /// </summary>
        /// <param name="mediator">The mediator.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="urlResolver">The urlResolver.</param>
        /// <param name="uiConfiguration">The UI configuration.</param>
        /// <param name="authorizationService">The authorization service.</param>
        public FhirController(
            IMediator mediator,
            ILogger<FhirController> logger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<FeatureConfiguration> uiConfiguration,
            IAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(uiConfiguration, nameof(uiConfiguration));
            EnsureArg.IsNotNull(uiConfiguration.Value, nameof(uiConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _mediator = mediator;
            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _authorizationService = authorizationService;
            _featureConfiguration = uiConfiguration.Value;
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
                default:
                    issueType = OperationOutcome.IssueType.Exception;
                    returnCode = HttpStatusCode.InternalServerError;
                    diagnosticInfo = Resources.GeneralInternalError;
                    break;
            }

            return FhirResult.Create(
                new OperationOutcome
                {
                    Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new OperationOutcome.IssueComponent
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = issueType,
                            Diagnostics = diagnosticInfo,
                        },
                    },
                }.ToResourceElement(), returnCode);
        }

        /// <summary>
        /// Creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        [HttpPost]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.Create)]
        [ServiceFilter(typeof(SearchParameterFilterAttribute))]
        public async Task<IActionResult> Create([FromBody] Resource resource)
        {
            RawResourceElement response = await _mediator.CreateResourceAsync(resource.ToResourceElement(), HttpContext.RequestAborted);

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
        [AuditEventType(AuditEventSubType.Create)]
        public async Task<IActionResult> ConditionalCreate([FromBody] Resource resource)
        {
            StringValues conditionalCreateHeader = HttpContext.Request.Headers[KnownFhirHeaders.IfNoneExist];

            Tuple<string, string>[] conditionalParameters = QueryHelpers.ParseQuery(conditionalCreateHeader)
                .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();

            UpsertResourceResponse createResponse = await _mediator.Send<UpsertResourceResponse>(new ConditionalCreateResourceRequest(resource.ToResourceElement(), conditionalParameters), HttpContext.RequestAborted);

            if (createResponse == null)
            {
                return Ok();
            }

            RawResourceElement response = createResponse.Outcome.RawResourceElement;

            return FhirResult.Create(response, HttpStatusCode.Created)
                .SetETagHeader()
                .SetLastModifiedHeader()
                .SetLocationHeader(_urlResolver);
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
        public async Task<IActionResult> Update([FromBody] Resource resource, [ModelBinder(typeof(WeakETagBinder))] WeakETag ifMatchHeader)
        {
            SaveOutcome response = await _mediator.UpsertResourceAsync(resource.ToResourceElement(), ifMatchHeader, HttpContext.RequestAborted);

            return ToSaveOutcomeResult(response);
        }

        /// <summary>
        /// Updates or creates a new resource
        /// </summary>
        /// <param name="resource">The resource.</param>
        [HttpPut]
        [Route(KnownRoutes.ResourceType)]
        [AuditEventType(AuditEventSubType.Update)]
        public async Task<IActionResult> ConditionalUpdate([FromBody] Resource resource)
        {
            IReadOnlyList<Tuple<string, string>> conditionalParameters = GetQueriesForSearch();

            UpsertResourceResponse response = await _mediator.Send<UpsertResourceResponse>(
                new ConditionalUpsertResourceRequest(resource.ToResourceElement(), conditionalParameters),
                HttpContext.RequestAborted);

            SaveOutcome saveOutcome = response.Outcome;

            return ToSaveOutcomeResult(saveOutcome);
        }

        private IActionResult ToSaveOutcomeResult(SaveOutcome saveOutcome)
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
        [Route(KnownRoutes.ResourceTypeById, Name = RouteNames.ReadResource)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> Read(string typeParameter, string idParameter)
        {
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);

            return FhirResult.Create(response)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        /// <summary>
        /// Returns the history of all resources in the system
        /// </summary>
        /// <param name="at">Instant for history to return.</param>
        /// <param name="since">Starting time for history to return (inclusive).</param>
        /// <param name="before">Ending time for history to return (exclusive)</param>
        /// <param name="count">Number of items to return.</param>
        /// <param name="ct">Continuation token.</param>
        [HttpGet]
        [Route(KnownRoutes.History, Name = RouteNames.History)]
        [AuditEventType(AuditEventSubType.HistorySystem)]
        public async Task<IActionResult> SystemHistory(
            [FromQuery(Name = KnownQueryParameterNames.At)] PartialDateTime at,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Before)] PartialDateTime before,
            [FromQuery(Name = KnownQueryParameterNames.Count)] int? count,
            string ct)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(since, before, at, count, ct, HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the history of a specific resource type
        /// </summary>
        /// <param name="typeParameter">The resource type.</param>
        /// <param name="at">Instant for history to return.</param>
        /// <param name="since">Starting time for history to return (inclusive).</param>
        /// <param name="before">Ending time for history to return (exclusive).</param>
        /// <param name="count">Number of items to return.</param>
        /// <param name="ct">Continuation token.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceTypeHistory, Name = RouteNames.HistoryType)]
        [AuditEventType(AuditEventSubType.HistoryType)]
        public async Task<IActionResult> TypeHistory(
            string typeParameter,
            [FromQuery(Name = KnownQueryParameterNames.At)] PartialDateTime at,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Before)] PartialDateTime before,
            [FromQuery(Name = KnownQueryParameterNames.Count)] int? count,
            string ct)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(typeParameter, since, before, at, count, ct, HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the history of a resource
        /// </summary>
        /// <param name="typeParameter">The resource type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="at">Instant for history to return.</param>
        /// <param name="since">Starting time for history to return (inclusive).</param>
        /// <param name="before">Ending time for hitory to return (exclusive).</param>
        /// <param name="count">Number of items to return.</param>
        /// <param name="ct">Continuation token.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceTypeByIdHistory, Name = RouteNames.HistoryTypeId)]
        [AuditEventType(AuditEventSubType.HistoryInstance)]
        public async Task<IActionResult> History(
            string typeParameter,
            string idParameter,
            [FromQuery(Name = KnownQueryParameterNames.At)] PartialDateTime at,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Before)] PartialDateTime before,
            [FromQuery(Name = KnownQueryParameterNames.Count)] int? count,
            string ct)
        {
            ResourceElement response = await _mediator.SearchResourceHistoryAsync(typeParameter, idParameter, since, before, at, count, ct, HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Reads the specified version of the resource.
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="vidParameter">The versionId.</param>
        [HttpGet]
        [Route(KnownRoutes.ResourceTypeByIdAndVid, Name = RouteNames.ReadResourceWithVersionRoute)]
        [AuditEventType(AuditEventSubType.VRead)]
        public async Task<IActionResult> VRead(string typeParameter, string idParameter, string vidParameter)
        {
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter, vidParameter), HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.OK)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        /// <summary>
        /// Deletes the specified resource
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        /// <param name="hardDelete">A flag indicating whether to hard-delete the resource or not.</param>
        [HttpDelete]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Delete)]
        public async Task<IActionResult> Delete(string typeParameter, string idParameter, [FromQuery]bool hardDelete)
        {
            DeleteResourceResponse response = await _mediator.DeleteResourceAsync(new ResourceKey(typeParameter, idParameter), hardDelete, HttpContext.RequestAborted);

            return FhirResult.NoContent().SetETagHeader(response.WeakETag);
        }

        /// <summary>
        /// Patches the specified resource
        /// </summary>
        /// <param name="typeParameter">The type.</param>
        /// <param name="idParameter">The identifier.</param>
        [HttpPatch]
        [Route(KnownRoutes.ResourceTypeById)]
        [AuditEventType(AuditEventSubType.Patch)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Controller methods won't be called if static.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1801:Review unused parameters", Justification = "Need the parameters for routing to work.")]
        public Task<IActionResult> Patch(string typeParameter, string idParameter)
        {
            throw new MethodNotAllowedException(Resources.PatchNotSupported);
        }

        /// <summary>
        /// Searches across all resource types.
        /// </summary>
        [HttpGet]
        [Route("", Name = RouteNames.SearchAllResources)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
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
        public async Task<IActionResult> SearchByResourceType(string typeParameter)
        {
            return await PerformSearch(typeParameter, GetQueriesForSearch());
        }

        private IReadOnlyList<Tuple<string, string>> GetQueriesForSearch()
        {
            IReadOnlyList<Tuple<string, string>> queries = Array.Empty<Tuple<string, string>>();

            if (Request.Query != null)
            {
                queries = Request.Query
                    .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();
            }

            return queries;
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
        [AllowAnonymous]
        [Route(KnownRoutes.Metadata, Name = RouteNames.Metadata)]
        public async Task<IActionResult> Metadata()
        {
            ResourceElement response = await _mediator.GetCapabilitiesAsync(HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }

        /// <summary>
        /// Returns the list of versions the server supports along with the default version it will use if no fhirVersion parameter is present.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
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
        public async Task<IActionResult> BatchAndTransactions([FromBody] Resource bundle)
        {
            ResourceElement bundleResponse = await _mediator.PostBundle(bundle.ToResourceElement());

            return FhirResult.Create(bundleResponse);
        }
    }
}
