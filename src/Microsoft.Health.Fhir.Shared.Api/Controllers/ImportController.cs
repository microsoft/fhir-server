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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class ImportController : Controller
    {
        /*
         * We are currently hardcoding the routing attribute to be specific to BulkImport and
         * get forwarded to this controller. As we add more operations we would like to resolve
         * the routes in a more dynamic manner. One way would be to use a regex route constraint
         * - eg: "{operation:regex(^\\$([[a-zA-Z]]+))}" - and use the appropriate operation handler.
         * Another way would be to use the capability statement to dynamically determine what operations
         * are supported.
         * It would be easier to determine what pattern to follow once we have built support for a couple
         * of operations. Then we can refactor this controller accordingly.
         */

        private readonly IReadOnlyList<string> allowedImportFormat = new List<string> { "application/fhir+ndjson" };
        private readonly IReadOnlyList<string> allowedStorageType = new List<string> { "azure-blob" };
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly FeatureConfiguration _features;
        private readonly ILogger<ImportController> _logger;
        private readonly ImportTaskConfiguration _importConfig;

        public ImportController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<FeatureConfiguration> features,
            ILogger<ImportController> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Import, nameof(operationsConfig));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(features?.Value, nameof(features));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _importConfig = operationsConfig.Value.Import;
            _urlResolver = urlResolver;
            _features = features.Value;
            _mediator = mediator;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.Import)]
        [ServiceFilter(typeof(ValidateImportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Import)]
        public async Task<IActionResult> Import([FromBody] Parameters importTaskParameters)
        {
            CheckIfImportIsEnabled();
            ImportRequest importRequest = importTaskParameters?.ExtractImportRequest();
            ValidateImportRequestConfiguration(importRequest);

            _logger.LogInformation("Import Mode {ImportMode}", importRequest.Mode);
            var initialLoad = ImportMode.InitialLoad.ToString().Equals(importRequest.Mode, StringComparison.OrdinalIgnoreCase);
            if (!initialLoad
                && !ImportMode.IncrementalLoad.ToString().Equals(importRequest.Mode, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(importRequest.Mode))
            {
                throw new RequestNotValidException(Resources.ImportModeIsNotRecognized);
            }

            if (initialLoad && !importRequest.Force && !_importConfig.InitialImportMode)
            {
                throw new RequestNotValidException(Resources.InitialImportModeNotEnabled);
            }

            CreateImportResponse response = await _mediator.ImportAsync(
                 _fhirRequestContextAccessor.RequestContext.Uri,
                 importRequest.InputFormat,
                 importRequest.InputSource,
                 importRequest.Input,
                 importRequest.StorageDetail,
                 initialLoad ? ImportMode.InitialLoad : ImportMode.IncrementalLoad, // default to incremental mode
                 HttpContext.RequestAborted);

            var bulkImportResult = ImportResult.Accepted();
            bulkImportResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Import, response.TaskId);
            return bulkImportResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.ImportJobLocation, Name = RouteNames.CancelImport)]
        [AuditEventType(AuditEventSubType.Import)]
        public async Task<IActionResult> CancelImport(long idParameter)
        {
            CancelImportResponse response = await _mediator.CancelImportAsync(idParameter, HttpContext.RequestAborted);

            _logger.LogInformation("CancelImport {StatusCode}", response.StatusCode);
            return new ImportResult(response.StatusCode);
        }

        [HttpGet]
        [Route(KnownRoutes.ImportJobLocation, Name = RouteNames.GetImportStatusById)]
        [AuditEventType(AuditEventSubType.Import)]
        public async Task<IActionResult> GetImportStatusById(long idParameter)
        {
            var getBulkImportResult = await _mediator.GetImportStatusAsync(
                idParameter,
                HttpContext.RequestAborted);

            // If the job is complete, we need to return 200 along with the completed data to the client.
            // Else we need to return 202 - Accepted.
            ImportResult bulkImportActionResult;
            if (getBulkImportResult.StatusCode == HttpStatusCode.OK)
            {
                bulkImportActionResult = ImportResult.Ok(getBulkImportResult.JobResult);
                bulkImportActionResult.SetContentTypeHeader(OperationsConstants.BulkImportContentTypeHeaderValue);
            }
            else
            {
                if (getBulkImportResult.JobResult == null)
                {
                    bulkImportActionResult = ImportResult.Accepted();
                }
                else
                {
                    bulkImportActionResult = ImportResult.Accepted(getBulkImportResult.JobResult);
                    bulkImportActionResult.SetContentTypeHeader(OperationsConstants.BulkImportContentTypeHeaderValue);
                }
            }

            return bulkImportActionResult;
        }

        private void CheckIfImportIsEnabled()
        {
            if (!_importConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.Import));
            }
        }

        private void ValidateImportRequestConfiguration(ImportRequest importData)
        {
            if (importData == null)
            {
                _logger.LogInformation("Failed to deserialize import request body as import configuration.");
                throw new RequestNotValidException(Resources.ImportRequestNotValid);
            }

            var inputFormat = importData.InputFormat;
            if (!allowedImportFormat.Any(s => s.Equals(inputFormat, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RequestNotValidException(string.Format(Resources.ImportRequestValueNotValid, nameof(inputFormat)));
            }

            var storageDetails = importData.StorageDetail;
            if (storageDetails != null && !allowedStorageType.Any(s => s.Equals(storageDetails.Type, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RequestNotValidException(string.Format(Resources.ImportRequestValueNotValid, nameof(storageDetails)));
            }

            var input = importData.Input;
            if (input == null || input.Count == 0)
            {
                throw new RequestNotValidException(string.Format(Resources.ImportRequestValueNotValid, nameof(input)));
            }

            foreach (var item in input)
            {
                if (!Enum.IsDefined(typeof(ResourceType), item.Type))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, item.Type));
                }

                if (item.Url == null || !string.IsNullOrEmpty(item.Url.Query))
                {
                    throw new RequestNotValidException(string.Format(Resources.ImportRequestValueNotValid, "input.url"));
                }
            }
        }
    }
}
