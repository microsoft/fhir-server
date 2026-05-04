// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
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
using Microsoft.Health.Fhir.Core.Features;
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
        private readonly ImportJobConfiguration _importConfig;
        private readonly Uri _configuredStorageAccountUri;

        public ImportController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<FeatureConfiguration> features,
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<ImportController> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Import, nameof(operationsConfig));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(features?.Value, nameof(features));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _importConfig = operationsConfig.Value.Import;
            _urlResolver = urlResolver;
            _features = features.Value;
            _configuredStorageAccountUri = GetConfiguredStorageAccountUri(integrationDataStoreConfiguration.Value);
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
                 importRequest.AllowNegativeVersions,
                 importRequest.ErrorContainerName,
                 importRequest.EventualConsistency,
                 importRequest.ProcessingUnitBytesToRead,
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
        public async Task<IActionResult> GetImportStatusById(long idParameter, [FromQuery(Name = KnownQueryParameterNames.ReturnDetails)] bool returnDetails)
        {
            var getBulkImportResult = await _mediator.GetImportStatusAsync(
                idParameter,
                HttpContext.RequestAborted,
                returnDetails);

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

            // Ensure that the server has a valid storage account configured for import operations.
            if (_configuredStorageAccountUri == null)
            {
                throw new RequestNotValidException(Resources.ImportStorageAccountNotConfigured);
            }

            var duplicateInputUrls = input.GroupBy(item => item.Url).Where(group => group.Count() > 1).Select(group => group.Key);

            if (duplicateInputUrls.Any())
            {
                var duplicateUrlString = string.Join(", ", duplicateInputUrls);
                throw new RequestNotValidException(string.Format(Resources.ImportRequestDuplicateInputFiles, duplicateUrlString));
            }

            foreach (var item in input)
            {
                if (!string.IsNullOrEmpty(item.Type) && !Enum.IsDefined(typeof(ResourceType), item.Type))
                {
                    throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, item.Type));
                }

                if (item.Url == null || !item.Url.IsAbsoluteUri || !string.IsNullOrEmpty(item.Url.Query))
                {
                    throw new RequestNotValidException(string.Format(Resources.ImportRequestValueNotValid, "input.url"));
                }

                if (!IsConfiguredStorageAccountEndpoint(item.Url))
                {
                    throw new RequestNotValidException(Resources.ImportRequestInputUrlStorageEndpointMismatch);
                }
            }

            if (input.Any(i => i.Type == "SearchParameter"))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, "SearchParameter"));
            }
        }

        private bool IsConfiguredStorageAccountEndpoint(Uri inputUri)
        {
            if (_configuredStorageAccountUri == null)
            {
                return false;
            }

            // Match scheme and hostname only. The hostname is the security boundary; port is
            // intentionally not checked because callers may specify a port that differs from the
            // configured URI (e.g. Azure Storage always uses the default HTTPS port in production).
            return string.Equals(inputUri.Scheme, _configuredStorageAccountUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(inputUri.IdnHost, _configuredStorageAccountUri.IdnHost, StringComparison.OrdinalIgnoreCase);
        }

        private static Uri GetConfiguredStorageAccountUri(IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            if (Uri.TryCreate(integrationDataStoreConfiguration.StorageAccountUri, UriKind.Absolute, out Uri storageAccountUri))
            {
                return storageAccountUri;
            }

            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountConnection))
            {
                return null;
            }

            if (integrationDataStoreConfiguration.StorageAccountConnection.StartsWith("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return new BlobServiceClient(integrationDataStoreConfiguration.StorageAccountConnection).Uri;
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            // Derive the default account endpoint from AccountName so explicit BlobEndpoint overrides
            // cannot allow arbitrary hosts. This supports both full and minimal connection strings
            // (e.g. AccountName-only when using managed identity or other credential providers).
            string accountName = GetConnectionStringValue(integrationDataStoreConfiguration.StorageAccountConnection, "AccountName");
            if (accountName == null)
            {
                return null;
            }

            string defaultEndpointsProtocol = GetConnectionStringValue(integrationDataStoreConfiguration.StorageAccountConnection, "DefaultEndpointsProtocol") ?? Uri.UriSchemeHttps;
            string endpointSuffix = GetConnectionStringValue(integrationDataStoreConfiguration.StorageAccountConnection, "EndpointSuffix") ?? "core.windows.net";

            return Uri.TryCreate($"{defaultEndpointsProtocol}://{accountName}.blob.{endpointSuffix}", UriKind.Absolute, out Uri connectionStringStorageAccountUri)
                ? connectionStringStorageAccountUri
                : null;
        }

        private static string GetConnectionStringValue(string connectionString, string key)
        {
            foreach (string segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
                if (separatorIndex > 0 && string.Equals(segment.Substring(0, separatorIndex), key, StringComparison.OrdinalIgnoreCase))
                {
                    string value = segment.Substring(separatorIndex + 1);
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }
    }
}
