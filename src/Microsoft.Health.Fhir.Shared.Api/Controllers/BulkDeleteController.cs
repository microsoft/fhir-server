// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class BulkDeleteController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IUrlResolver _urlResolver;

        private readonly HashSet<string> _excludedParameters = new(new PropertyEqualityComparer<string>(StringComparison.OrdinalIgnoreCase, s => s))
        {
            KnownQueryParameterNames.BulkHardDelete,
            KnownQueryParameterNames.HardDelete,
            KnownQueryParameterNames.PurgeHistory,
            KnownQueryParameterNames.ExcludedResourceTypes,
            KnownQueryParameterNames.RemoveReferences,
        };

        public BulkDeleteController(
            IMediator mediator,
            ISearchParameterOperations searchParameterOperations,
            IUrlResolver urlResolver)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDelete)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDelete(
            HardDeleteModel hardDeleteModel,
            [FromQuery(Name = KnownQueryParameterNames.PurgeHistory)] bool purgeHistory,
            [FromQuery(Name = KnownQueryParameterNames.RemoveReferences)] bool removeReferences,
            [FromQuery(Name = KnownQueryParameterNames.ExcludedResourceTypes)] string excludedResourceTypes = null)
        {
            return await SendDeleteRequest(null, hardDeleteModel.IsHardDelete, purgeHistory, false, excludedResourceTypes, removeReferences);
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteResourceType)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDeleteByResourceType(
            string typeParameter,
            HardDeleteModel hardDeleteModel,
            [FromQuery(Name = KnownQueryParameterNames.PurgeHistory)] bool purgeHistory,
            [FromQuery(Name = KnownQueryParameterNames.RemoveReferences)] bool removeReferences,
            [FromQuery(Name = KnownQueryParameterNames.ExcludedResourceTypes)] string excludedResourceTypes = null)
        {
            return await SendDeleteRequest(typeParameter, hardDeleteModel.IsHardDelete, purgeHistory, false, excludedResourceTypes, removeReferences);
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteSoftDeleted)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDeleteSoftDeleted([FromQuery(Name = KnownQueryParameterNames.PurgeHistory)] bool purgeHistory)
        {
            return await SendDeleteRequest(null, true, purgeHistory, true, null, false);
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteSoftDeletedResourceType)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDeleteSoftDeletedByResourceType(string typeParameter, [FromQuery(Name = KnownQueryParameterNames.PurgeHistory)] bool purgeHistory)
        {
            return await SendDeleteRequest(typeParameter, true, purgeHistory, true, null, false);
        }

        [HttpGet]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.GetBulkDeleteStatusById)]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> GetBulkDeleteStatusById(long idParameter)
        {
            var result = await _mediator.GetBulkDeleteStatusAsync(idParameter, HttpContext.RequestAborted);
            var actionResult = JobResult.FromResults(result.Results, result.Issues, result.HttpStatusCode);
            if (result.HttpStatusCode == System.Net.HttpStatusCode.Accepted)
            {
                actionResult.Headers[KnownHeaders.Progress] = Resources.InProgress;
            }

            return actionResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.CancelBulkDelete)]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> CancelBulkDelete(long idParameter)
        {
            var result = await _mediator.CancelBulkDeleteAsync(idParameter, HttpContext.RequestAborted);
            return new JobResult(result.StatusCode);
        }

        private async Task<IActionResult> SendDeleteRequest(string typeParameter, bool hardDelete, bool purgeHistory, bool softDeleteCleanup, string excludedResourceTypes, bool removeReferences)
        {
            IList<Tuple<string, string>> searchParameters = Request.GetQueriesForSearch().ToList();

            searchParameters = searchParameters.Where(param => !_excludedParameters.Contains(param.Item1)).ToList();

            if (softDeleteCleanup && searchParameters.Any(param => param.Item1 != KnownQueryParameterNames.LastUpdated))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedParameter, searchParameters.Where(param => param.Item1 != KnownQueryParameterNames.LastUpdated).Select(param => param.Item1).Aggregate((param, next) => param += ", " + next)));
            }

            DeleteOperation deleteOperation = (hardDelete, purgeHistory) switch
            {
                { hardDelete: true } => DeleteOperation.HardDelete,
                { purgeHistory: true } => DeleteOperation.PurgeHistory,
                _ => DeleteOperation.SoftDelete,
            };

            var excludedResourceTypesList = new List<string>();
            if (!string.IsNullOrEmpty(excludedResourceTypes))
            {
                excludedResourceTypesList = excludedResourceTypes.Split(',').ToList();
            }

            if (WillAffectSearchParameters(typeParameter, excludedResourceTypesList))
            {
                await _searchParameterOperations.EnsureNoActiveReindexJobAsync(HttpContext.RequestAborted);
            }

            CreateBulkDeleteResponse result = await _mediator.BulkDeleteAsync(deleteOperation, typeParameter, searchParameters, softDeleteCleanup, excludedResourceTypesList, removeReferences, HttpContext.RequestAborted);

            var response = JobResult.Accepted();
            response.SetContentLocationHeader(_urlResolver, OperationsConstants.BulkDelete, result.Id.ToString());
            return response;
        }

        // Determines whether the request will deterministically affect SearchParameter resources.
        // Only the explicit-type case is checked here (e.g. _type=SearchParameter); for system-wide
        // requests the precise check is deferred to the orchestrator/processing job, which evaluates
        // the user's search filters against actual record counts. This avoids false-positive
        // conflicts on system-wide deletes that are filtered (e.g. by _tag) and would not actually
        // touch any SearchParameter resources.
        private static bool WillAffectSearchParameters(string resourceType, IList<string> excludedResourceTypes)
        {
            if (excludedResourceTypes?.Any(x => string.Equals(x, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return false;
            }

            return resourceType.SplitByOrSeparator().Any(x => string.Equals(x, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase));
        }
    }
}
