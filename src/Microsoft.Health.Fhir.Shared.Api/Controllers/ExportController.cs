﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Identity.Client;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
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
        private readonly ConvertDataConfiguration _convertConfig;
        private readonly ArtifactStoreConfiguration _artifactStoreConfig;
        private readonly FeatureConfiguration _features;
        private readonly IFhirRuntimeConfiguration _fhirConfig;

        public ExportController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<ArtifactStoreConfiguration> artifactStoreConfig,
            IOptions<FeatureConfiguration> features,
            IFhirRuntimeConfiguration fhirConfig)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(artifactStoreConfig, nameof(artifactStoreConfig));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Export, nameof(operationsConfig));
            EnsureArg.IsNotNull(features?.Value, nameof(features));
            EnsureArg.IsNotNull(fhirConfig, nameof(fhirConfig));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _exportConfig = operationsConfig.Value.Export;
            _convertConfig = operationsConfig.Value.ConvertData;
            _artifactStoreConfig = artifactStoreConfig.Value;
            _features = features.Value;
            _fhirConfig = fhirConfig;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> Export(
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Till)] PartialDateTime till,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.IsParallel)] bool? isParallel = null,
            [FromQuery(Name = KnownQueryParameterNames.IncludeAssociatedData)] string includeAssociatedData = null,
            [FromQuery(Name = KnownQueryParameterNames.MaxCount)] uint maxCount = 0,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationCollectionReference)] string anonymizationConfigCollectionReference = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag = null)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag);
            (bool includeHistory, bool includeDeleted) = ValidateAndParseIncludeAssociatedData(includeAssociatedData, typeFilter);

            return await SendExportRequest(
                exportType: ExportJobType.All,
                since: since,
                till: till,
                filters: typeFilter,
                resourceType: resourceType,
                containerName: containerName,
                formatName: formatName,
                isParallel: isParallel,
                includeHistory: includeHistory,
                includeDeleted: includeDeleted,
                maxCount: maxCount,
                anonymizationConfigCollectionReference: anonymizationConfigCollectionReference,
                anonymizationConfigLocation: anonymizationConfigLocation,
                anonymizationConfigFileETag: anonymizationConfigFileETag);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> ExportResourceType(
            string typeParameter,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Till)] PartialDateTime till,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.MaxCount)] uint maxCount = 0,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationCollectionReference)] string anonymizationConfigCollectionReference = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag = null)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag);

            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(typeParameter, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return await SendExportRequest(
                exportType: ExportJobType.Patient,
                since: since,
                till: till,
                filters: typeFilter,
                resourceType: resourceType,
                containerName: containerName,
                formatName: formatName,
                isParallel: false,
                maxCount: maxCount,
                anonymizationConfigCollectionReference: anonymizationConfigCollectionReference,
                anonymizationConfigLocation: anonymizationConfigLocation,
                anonymizationConfigFileETag: anonymizationConfigFileETag);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> ExportResourceTypeById(
            string typeParameter,
            string idParameter,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Till)] PartialDateTime till,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string resourceType,
            [FromQuery(Name = KnownQueryParameterNames.Container)] string containerName,
            [FromQuery(Name = KnownQueryParameterNames.TypeFilter)] string typeFilter,
            [FromQuery(Name = KnownQueryParameterNames.Format)] string formatName,
            [FromQuery(Name = KnownQueryParameterNames.MaxCount)] uint maxCount = 0,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationCollectionReference)] string anonymizationConfigCollectionReference = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationLocation)] string anonymizationConfigLocation = null,
            [FromQuery(Name = KnownQueryParameterNames.AnonymizationConfigurationFileEtag)] string anonymizationConfigFileETag = null)
        {
            CheckIfExportIsEnabled();
            ValidateForAnonymizedExport(containerName, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag);

            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(typeParameter, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(idParameter))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return await SendExportRequest(
                exportType: ExportJobType.Group,
                since: since,
                till: till,
                filters: typeFilter,
                resourceType: resourceType,
                groupId: idParameter,
                containerName: containerName,
                formatName: formatName,
                isParallel: false,
                maxCount: maxCount,
                anonymizationConfigCollectionReference: anonymizationConfigCollectionReference,
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
            PartialDateTime till,
            string filters,
            string resourceType = null,
            string groupId = null,
            string containerName = null,
            string formatName = null,
            bool? isParallel = true,
            bool includeHistory = false,
            bool includeDeleted = false,
            uint maxCount = 0,
            string anonymizationConfigCollectionReference = null,
            string anonymizationConfigLocation = null,
            string anonymizationConfigFileETag = null)
        {
            bool isParallelWithDefault = GetExportParallelSetting(isParallel, _fhirConfig);

            CreateExportResponse response = await _mediator.ExportAsync(
                _fhirRequestContextAccessor.RequestContext.Uri,
                exportType,
                resourceType,
                since,
                till,
                filters,
                groupId,
                containerName,
                formatName,
                isParallelWithDefault,
                includeDeleted,
                includeHistory,
                maxCount,
                anonymizationConfigCollectionReference,
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

        private void ValidateForAnonymizedExport(string containerName, string anonymizationConfigCollectionReference, string anonymizationConfigLocation, string anonymizationConfigFileETag)
        {
            if (!string.IsNullOrWhiteSpace(anonymizationConfigLocation) || !string.IsNullOrWhiteSpace(anonymizationConfigFileETag) || !string.IsNullOrWhiteSpace(anonymizationConfigCollectionReference))
            {
                // Check if anonymizedExport is enabled
                if (!_features.SupportsAnonymizedExport)
                {
                    throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.AnonymizedExport));
                }

                CheckContainerNameAndConfigLocationForAnonymizedExport(containerName, anonymizationConfigLocation);
                if (!string.IsNullOrWhiteSpace(anonymizationConfigCollectionReference))
                {
                    CheckReferenceAndETagParameterConflictForAnonymizedExport(anonymizationConfigCollectionReference, anonymizationConfigFileETag);
                    CheckConfigCollectionReferenceIsValid(anonymizationConfigCollectionReference);
                    CheckIfConfigCollectionReferenceIsConfigured(anonymizationConfigCollectionReference);
                }
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

        private static void CheckConfigCollectionReferenceIsValid(string anonymizationConfigCollectionReference)
        {
            if (!ImageInfo.IsValidImageReference(anonymizationConfigCollectionReference))
            {
                throw new RequestNotValidException(string.Format(Resources.InvalidAnonymizationConfigCollectionReference, anonymizationConfigCollectionReference));
            }
        }

        private static void CheckReferenceAndETagParameterConflictForAnonymizedExport(string anonymizationConfigCollectionReference, string eTag)
        {
            if (!string.IsNullOrEmpty(anonymizationConfigCollectionReference) && !string.IsNullOrEmpty(eTag))
            {
                throw new RequestNotValidException(Resources.AnonymizationParameterConflict);
            }
        }

        private void CheckIfConfigCollectionReferenceIsConfigured(string anonymizationConfigCollectionReference)
        {
            var ociImage = ImageInfo.CreateFromImageReference(anonymizationConfigCollectionReference);

            // For compatibility purpose.
            // Return if registryServer has been configured in previous ConvertDataConfiguration.
            if (_convertConfig.ContainerRegistryServers.Any(server =>
                string.Equals(server, ociImage.Registry, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!_artifactStoreConfig.OciArtifacts.Any(ociArtifact =>
                    ociArtifact.ContainsOciImage(ociImage.Registry, ociImage.ImageName, ociImage.Digest)))
            {
                throw new RequestNotValidException(string.Format(Resources.AnonymizationConfigCollectionNotConfigured, anonymizationConfigCollectionReference));
            }
        }

        private static (bool includeHistory, bool includeDeleted) ValidateAndParseIncludeAssociatedData(string associatedData, string typeFilter)
        {
            var associatedDataParams = (associatedData ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var possibleParams = new List<string> { "_history", "_deleted" };
            var invalidParams = associatedDataParams.Where(param => !possibleParams.Contains(param)).ToList();

            if (invalidParams.Any())
            {
                throw new RequestNotValidException(string.Format(Resources.InvalidExportAssociatedDataParameter, string.Join(',', invalidParams)));
            }

            bool includeHistory = associatedDataParams.Contains("_history");
            bool includeDeleted = associatedDataParams.Contains("_deleted");

            if ((includeHistory || includeDeleted) && !string.IsNullOrWhiteSpace(typeFilter))
            {
                throw new RequestNotValidException(string.Format(Resources.TypeFilterNotSupportedWithHistoryOrDeletedExport, "_history and _deleted"));
            }

            return (includeHistory, includeDeleted);
        }

        private static bool GetExportParallelSetting(bool? isParallelFromRequest, IFhirRuntimeConfiguration fhirConfig)
        {
            if (isParallelFromRequest is not null)
            {
                return isParallelFromRequest.Value;
            }

            // Api For FHIR defaults to non-parallel export.
            if (fhirConfig is AzureApiForFhirRuntimeConfiguration)
            {
                return false;
            }

            return true;
        }
    }
}
