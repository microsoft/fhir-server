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
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class BulkImportController : Controller
    {
        /*
         * We are currently hardcoding the routing attribute to be specific to Export and
         * get forwarded to this controller. As we add more operations we would like to resolve
         * the routes in a more dynamic manner. One way would be to use a regex route constraint
         * - eg: "{operation:regex(^\\$([[a-zA-Z]]+))}" - and use the appropriate operation handler.
         * Another way would be to use the capability statement to dynamically determine what operations
         * are supported.
         * It would be easier to determine what pattern to follow once we have built support for a couple
         * of operations. Then we can refactor this controller accordingly.
         */

        private readonly IReadOnlyList<string> allowedImportFormat = new List<string> { "application/fhir+ndjson" };
        private readonly IReadOnlyList<string> allowedStorageType = new List<string> { "https", "aws-s3", "gcp-bucket", "azure-blob" };
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<BulkImportController> _logger;
        private readonly BulkImportJobConfiguration _bulkImportConfig;

        public BulkImportController (
            IMediator mediator,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<BulkImportController> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _bulkImportConfig = operationsConfig.Value.BulkImport;
            _urlResolver = urlResolver;
            _mediator = mediator;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.BulkImport)]
        [ServiceFilter(typeof(ValidateBulkImportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkImport)]
        public async Task<IActionResult> BulkImport([FromBody] BulkImportRequestConfiguration importRequestConfig)
        {
            CheckIfBulkImportIsEnabled();
            ValidateImportRequestConfiguration(importRequestConfig);

            CreateBulkImportResponse response = await _mediator.BulkImportAsync(
                 _fhirRequestContextAccessor.FhirRequestContext.Uri,
                 importRequestConfig,
                 HttpContext.RequestAborted);

            var bulkImportResult = BulkImportResult.Accepted();
            bulkImportResult.SetContentLocationHeader(_urlResolver, OperationsConstants.BulkImport, response.JobId);
            return bulkImportResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkImportJobLocation, Name = RouteNames.CancelBulkImport)]
        [AuditEventType(AuditEventSubType.BulkImport)]
        public async Task<IActionResult> CancelBulkImport(string idParameter)
        {
            CancelBulkImportResponse response = await _mediator.CancelBulkImportAsync(idParameter, HttpContext.RequestAborted);

            return new BulkImportResult(response.StatusCode);
        }

        [HttpGet]
        [Route(KnownRoutes.BulkImportJobLocation, Name = RouteNames.GetBulkImportStatusById)]
        [ServiceFilter(typeof(ValidateBulkImportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkImport)]
        public async Task<IActionResult> GetBulkImportStatusById(string idParameter)
        {
            var getBulkImportResult = await _mediator.GetBulkImportStatusAsync(
                _fhirRequestContextAccessor.FhirRequestContext.Uri,
                idParameter,
                HttpContext.RequestAborted);

            // If the job is complete, we need to return 200 along with the completed data to the client.
            // Else we need to return 202 - Accepted.
            BulkImportResult bulkImportActionResult;
            if (getBulkImportResult.StatusCode == HttpStatusCode.OK)
            {
                bulkImportActionResult = BulkImportResult.Ok(getBulkImportResult.JobResult);
                bulkImportActionResult.SetContentTypeHeader(OperationsConstants.BulkImportContentTypeHeaderValue);
            }
            else
            {
                bulkImportActionResult = BulkImportResult.Accepted();
            }

            return bulkImportActionResult;
        }

        private void CheckIfBulkImportIsEnabled()
        {
            if (!_bulkImportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.BulkImport));
            }
        }

        private void ValidateImportRequestConfiguration(BulkImportRequestConfiguration importData)
        {
            if (importData == null)
            {
                _logger.LogInformation("Failed to deserialize import request body as import configuration.");
                throw new RequestNotValidException(Resources.BulkImportRequestConfigurationNotValid);
            }

            var inputFormat = importData.InputFormat;
            if (!allowedImportFormat.Any(s => s.Equals(inputFormat, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RequestNotValidException(string.Format(Resources.BulkImportRequestConfigurationValueNotValid, nameof(inputFormat)));
            }

            var storageDetails = importData.StorageDetail;
            if (storageDetails != null && !allowedStorageType.Any(s => s.Equals(storageDetails.Type, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RequestNotValidException(string.Format(Resources.BulkImportRequestConfigurationValueNotValid, nameof(storageDetails)));
            }

            var input = importData.Input;
            if (input == null)
            {
                throw new RequestNotValidException(string.Format(Resources.BulkImportRequestConfigurationValueNotValid, nameof(input)));
            }

            foreach (var item in input)
            {
                if (!Enum.IsDefined(typeof(Hl7.Fhir.Model.ResourceType), item.Type))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, item.Type));
                }
            }
        }
    }
}
