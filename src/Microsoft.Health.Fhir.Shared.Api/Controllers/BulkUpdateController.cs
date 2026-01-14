// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class BulkUpdateController : Controller
    {
        private readonly IMediator _mediator;
        private readonly IUrlResolver _urlResolver;
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;
        private readonly OperationsConfiguration _operationConfiguration;

        public BulkUpdateController(
            IMediator mediator,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationConfiguration,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            _operationConfiguration = EnsureArg.IsNotNull(operationConfiguration.Value, nameof(operationConfiguration));
            _fhirRuntimeConfiguration = EnsureArg.IsNotNull(fhirRuntimeConfiguration, nameof(fhirRuntimeConfiguration));
        }

        [HttpPatch]
        [Route(KnownRoutes.BulkUpdate)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkUpdate)]
        public async Task<IActionResult> BulkUpdate([FromBody] Parameters paramsResource, [FromQuery(Name = KnownQueryParameterNames.IsParallel)] bool? isParallel = null, [FromQuery(Name = KnownQueryParameterNames.MaxCount)] uint maxCount = 0)
        {
            CheckIfOperationIsSupported();
            return await SendUpdateRequest(null, paramsResource, isParallel, maxCount);
        }

        [HttpPatch]
        [Route(KnownRoutes.BulkUpdateResourceType)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkUpdate)]
        public async Task<IActionResult> BulkUpdateByResourceType(string typeParameter, [FromBody] Parameters paramsResource, [FromQuery(Name = KnownQueryParameterNames.IsParallel)] bool? isParallel = null, [FromQuery(Name = KnownQueryParameterNames.MaxCount)] uint maxCount = 0)
        {
            CheckIfOperationIsSupported();
            return await SendUpdateRequest(typeParameter, paramsResource, isParallel, maxCount);
        }

        [HttpGet]
        [Route(KnownRoutes.BulkUpdateJobLocation, Name = RouteNames.GetBulkUpdateStatusById)]
        [AuditEventType(AuditEventSubType.BulkUpdate)]
        public async Task<IActionResult> GetBulkUpdateStatusById(long idParameter)
        {
            CheckIfOperationIsSupported();
            var result = await _mediator.GetBulkUpdateStatusAsync(idParameter, HttpContext.RequestAborted);
            var actionResult = JobResult.FromResults(result.Results, result.Issues, result.HttpStatusCode);
            if (result.HttpStatusCode == System.Net.HttpStatusCode.Accepted)
            {
                actionResult.Headers[KnownHeaders.Progress] = Resources.InProgress;
            }

            return actionResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkUpdateJobLocation, Name = RouteNames.CancelBulkUpdate)]
        [AuditEventType(AuditEventSubType.BulkUpdate)]
        public async Task<IActionResult> CancelBulkUpdate(long idParameter)
        {
            CheckIfOperationIsSupported();
            var result = await _mediator.CancelBulkUpdateAsync(idParameter, HttpContext.RequestAborted);
            return new JobResult(result.StatusCode);
        }

        private async Task<IActionResult> SendUpdateRequest(string typeParameter, Parameters parameters, bool? isParallel, uint maxCount = 0)
        {
            IList<Tuple<string, string>> searchParameters = Request.GetQueriesForSearch().ToList();

            CreateBulkUpdateResponse result = await _mediator.BulkUpdateAsync(typeParameter, searchParameters, parameters, isParallel ?? true, maxCount, HttpContext.RequestAborted);

            var response = JobResult.Accepted();
            response.SetContentLocationHeader(_urlResolver, OperationsConstants.BulkUpdate, result.Id.ToString());
            return response;
        }

        private void CheckIfOperationIsSupported()
        {
            if (!_operationConfiguration.BulkUpdate.Enabled || !string.Equals(_fhirRuntimeConfiguration.DataStore, KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase))
            {
                throw new RequestNotValidException(Fhir.Core.Resources.UnsupportedBulkUpdateOperation);
            }
        }
    }
}
