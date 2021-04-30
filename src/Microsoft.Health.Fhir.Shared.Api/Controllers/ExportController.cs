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
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class ExportController : Controller
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

        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly ExportJobConfiguration _exportConfig;
        private readonly FeatureConfiguration _features;
        private readonly ILogger<ExportController> _logger;

        public ExportController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<FeatureConfiguration> features,
            ILogger<ExportController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Export, nameof(operationsConfig));
            EnsureArg.IsNotNull(features?.Value, nameof(features));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _exportConfig = operationsConfig.Value.Export;
            _features = features.Value;
            _logger = logger;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> Export(
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigLocation, anonymizationConfigFileETag);

            return await SendExportRequest(
                exportType: ExportJobType.All,
                since: since,
                filters: typeFilter,
                resourceType: resourceType,
                containerName: containerName,
                formatName: formatName,
                anonymizationConfigLocation: anonymizationConfigLocation,
                anonymizationConfigFileETag: anonymizationConfigFileETag);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> ExportResourceType(
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag,
            string typeParameter)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigLocation, anonymizationConfigFileETag);

            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(typeParameter, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return await SendExportRequest(
                exportType: ExportJobType.Patient,
                since: since,
                filters: typeFilter,
                resourceType: resourceType,
                containerName: containerName,
                formatName: formatName,
                anonymizationConfigLocation: anonymizationConfigLocation,
                anonymizationConfigFileETag: anonymizationConfigFileETag);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> ExportResourceTypeById(
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag,
            string typeParameter,
            string idParameter)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigLocation, anonymizationConfigFileETag);

            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(typeParameter, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(idParameter))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return await SendExportRequest(
                exportType: ExportJobType.Group,
                since: since,
                filters: typeFilter,
                resourceType: resourceType,
                groupId: idParameter,
                containerName: containerName,
                formatName: formatName,
                anonymizationConfigLocation: anonymizationConfigLocation,
                anonymizationConfigFileETag: anonymizationConfigFileETag);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportJobLocation, Name = RouteNames.GetExportStatusById)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> GetExportStatusById(string idParameter)
        {
            var getExportResult = await _mediator.GetExportStatusAsync(
                _fhirRequestContextAccessor.RequestContext.Uri,
                idParameter,
                HttpContext.RequestAborted);

            // If the job is complete, we need to return 200 along with the completed data to the client.
            // Else we need to return 202 - Accepted.
            ExportResult exportActionResult;
            if (getExportResult.StatusCode == HttpStatusCode.OK)
            {
                exportActionResult = ExportResult.Ok(getExportResult.JobResult);
                exportActionResult.SetContentTypeHeader(OperationsConstants.ExportContentTypeHeaderValue);
            }
            else
            {
                exportActionResult = ExportResult.Accepted();
            }

            return exportActionResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.ExportJobLocation, Name = RouteNames.CancelExport)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> CancelExport(string idParameter)
        {
            CancelExportResponse response = await _mediator.CancelExportAsync(idParameter, HttpContext.RequestAborted);

            return new ExportResult(response.StatusCode);
        }

        private async Task<IActionResult> SendExportRequest(
            ExportJobType exportType,
            PartialDateTime since,
            string filters,
            string resourceType = null,
            string groupId = null,
            string containerName = null,
            string formatName = null,
            string anonymizationConfigLocation = null,
            string anonymizationConfigFileETag = null)
        {
            CreateExportResponse response = await _mediator.ExportAsync(
                _fhirRequestContextAccessor.RequestContext.Uri,
                exportType,
                resourceType,
                since,
                filters,
                groupId,
                containerName,
                formatName,
                anonymizationConfigLocation,
                anonymizationConfigFileETag,
                HttpContext.RequestAborted);

            var exportResult = ExportResult.Accepted();
            exportResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Export, response.JobId);
            return exportResult;
        }

        private void CheckIfExportIsEnabled()
        {
            if (!_exportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.Export));
            }
        }

        private void ValidateForAnonymizedExport(string containerName, string anonymizationConfigLocation, string anonymizationConfigFileETag)
        {
            if (!string.IsNullOrWhiteSpace(anonymizationConfigLocation) || !string.IsNullOrWhiteSpace(anonymizationConfigFileETag))
            {
                // Check if anonymizedExport is enabled
                if (!_features.SupportsAnonymizedExport)
                {
                    throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.AnonymizedExport));
                }

                CheckContainerNameAndConfigLocationForAnonymizedExport(containerName, anonymizationConfigLocation);
            }
        }

        private static void CheckContainerNameAndConfigLocationForAnonymizedExport(string containerName, string anonymizationConfigLocation)
        {
            if (string.IsNullOrWhiteSpace(anonymizationConfigLocation))
            {
                throw new RequestNotValidException(Resources.ConfigLocationRequiredForAnonymizedExport);
            }

            if (string.IsNullOrWhiteSpace(containerName))
            {
                throw new RequestNotValidException(Resources.ContainerIsRequiredForAnonymizedExport);
            }
        }
    }
}
