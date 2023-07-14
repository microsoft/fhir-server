// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Shared.Core.Extensions;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller that will handle all requests for OperationDefinition of
    /// operations that are supported by our fhir-server and that do not have
    /// an explicit definition in the HL7 website.
    /// </summary>
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class OperationDefinitionController : Controller
    {
        private readonly IMediator _mediator;
        private readonly OperationsConfiguration _operationConfiguration;
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;

        public OperationDefinitionController(
            IMediator mediator,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<FeatureConfiguration> featureConfig,
            IOptions<CoreFeatureConfiguration> coreFeatureConfig)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(operationsConfig?.Value, nameof(operationsConfig));
            EnsureArg.IsNotNull(featureConfig?.Value, nameof(featureConfig));
            EnsureArg.IsNotNull(coreFeatureConfig?.Value, nameof(coreFeatureConfig));

            _mediator = mediator;
            _operationConfiguration = operationsConfig.Value;
            _featureConfiguration = featureConfig.Value;
            _coreFeatureConfiguration = coreFeatureConfig.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.ReindexOperationDefinition, Name = RouteNames.ReindexOperationDefintion)]
        [AllowAnonymous]
        public async Task<IActionResult> ReindexOperationDefintion()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.Reindex);
        }

        [HttpGet]
        [Route(KnownRoutes.ResourceReindexOperationDefinition, Name = RouteNames.ResourceReindexOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> ResourceReindexOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.ResourceReindex);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportOperationDefinition, Name = RouteNames.ExportOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> ExportOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.Export);
        }

        [HttpGet]
        [Route(KnownRoutes.PatientExportOperationDefinition, Name = RouteNames.PatientExportOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> PatientExportOperationGetDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.PatientExport);
        }

        [HttpGet]
        [Route(KnownRoutes.GroupExportOperationDefinition, Name = RouteNames.GroupExportOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> GroupExportOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.GroupExport);
        }

        [HttpGet]
        [Route(KnownRoutes.AnonymizedExportOperationDefinition, Name = RouteNames.AnonymizedExportOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> AnonymizedExportOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.AnonymizedExport);
        }

        [HttpGet]
        [Route(KnownRoutes.ConvertDataOperationDefinition, Name = RouteNames.ConvertDataOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> ConvertDataOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.ConvertData);
        }

        [HttpGet]
        [Route(KnownRoutes.MemberMatchOperationDefinition, Name = RouteNames.MemberMatchOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> MemberMatchOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.MemberMatch);
        }

        [HttpGet]
        [Route(KnownRoutes.PurgeHistoryOperationDefinition, Name = RouteNames.PurgeHistoryDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> PurgeHistoryOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.PurgeHistory);
        }

        [HttpGet]
        [Route(KnownRoutes.BulkDeleteOperationDefinition, Name = RouteNames.BulkDeleteDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> BulkDeleteOperationDefinition()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.BulkDelete);
        }

        [HttpGet]
        [Route(KnownRoutes.SearchParametersStatusQuery, Name = RouteNames.SearchParameterStatusOperationDefinition)]
        [AllowAnonymous]
        public async Task<IActionResult> SearchParameterStatusOperationDefintion()
        {
            return await GetOperationDefinitionAsync(OperationsConstants.SearchParameterStatus);
        }

        private async Task<IActionResult> GetOperationDefinitionAsync(string operationName)
        {
            CheckIfOperationIsEnabledAndRespond(operationName);

            OperationDefinitionResponse response = await _mediator.GetOperationDefinitionAsync(operationName, HttpContext.RequestAborted);

            return FhirResult.Create(response.OperationDefinition, HttpStatusCode.OK);
        }

        private void CheckIfOperationIsEnabledAndRespond(string operationName)
        {
            bool operationEnabled = false;
            switch (operationName)
            {
                case OperationsConstants.Export:
                case OperationsConstants.GroupExport:
                case OperationsConstants.PatientExport:
                    operationEnabled = _operationConfiguration.Export.Enabled;
                    break;
                case OperationsConstants.AnonymizedExport:
                    operationEnabled = _featureConfiguration.SupportsAnonymizedExport;
                    break;
                case OperationsConstants.Reindex:
                case OperationsConstants.ResourceReindex:
                    operationEnabled = _operationConfiguration.Reindex.Enabled;
                    break;
                case OperationsConstants.ConvertData:
                    operationEnabled = _operationConfiguration.ConvertData.Enabled;
                    break;
                case OperationsConstants.MemberMatch:
                case OperationsConstants.PurgeHistory:
                    operationEnabled = true;
                    break;
                case OperationsConstants.SearchParameterStatus:
                    operationEnabled = _coreFeatureConfiguration.SupportsSelectableSearchParameters;
                    break;
                case OperationsConstants.BulkDelete:
                    operationEnabled = _coreFeatureConfiguration.SupportsBulkDelete;
                    break;
                default:
                    break;
            }

            if (!operationEnabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, operationName));
            }
        }
    }
}
