// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class ReindexController : Controller
    {
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly ReindexJobConfiguration _config;
        private readonly ILogger<ReindexController> _logger;

        public ReindexController(
            IMediator mediator,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<ReindexController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Reindex, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _config = operationsConfig.Value.Reindex;
            _logger = logger;
        }

        [HttpGet]
        [Route(KnownRoutes.Reindex)]
        [ServiceFilter(typeof(ValidateReindexRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> ListReindexJobs([FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since)
        {
            CheckIfReindexIsEnabledAndRespond();

            CreateReindexResponse response = await _mediator.ExportAsync

            var exportResult = ExportResult.Accepted();
            exportResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Export, response.JobId);

            return exportResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.ReindexJobLocation)]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> CancelReindex(string idParameter)
        {
            CheckIfReindexIsEnabledAndRespond();

            CancelReindexResponse response = await _mediator.CancelReindexAsync(idParameter, HttpContext.RequestAborted);

            return new CancelReindexResult(response.StatusCode);
        }

        /// <summary>
        /// Provide appropriate response if Reindex is not enabled
        /// </summary>
        private void CheckIfReindexIsEnabledAndRespond()
        {
            if (!_config.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.Reindex));
            }

            return;
        }
    }
}
