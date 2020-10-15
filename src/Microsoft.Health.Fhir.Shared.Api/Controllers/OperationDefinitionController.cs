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

        public OperationDefinitionController(IMediator mediator, IOptions<OperationsConfiguration> operationsConfig)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(operationsConfig?.Value, nameof(operationsConfig));

            _mediator = mediator;
            _operationConfiguration = operationsConfig.Value;
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
                case OperationsConstants.Reindex:
                case OperationsConstants.ResourceReindex:
                    operationEnabled = _operationConfiguration.Reindex.Enabled;
                    break;
                default:
                    break;
            }

            if (!operationEnabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, operationName));
            }

            return;
        }
    }
}
