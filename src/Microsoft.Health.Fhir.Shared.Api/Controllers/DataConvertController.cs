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
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    public class DataConvertController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ILogger _logger;
        private readonly DataConvertConfiguration _config;
        private static HashSet<string> _supportedParams = GetSupportedParams();

        private const char ImageRegistryDelimiter = '/';

        public DataConvertController(
            IMediator mediator,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<DataConvertController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(operationsConfig?.Value?.DataConvert, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _config = operationsConfig.Value.DataConvert;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.DataConvert)]
        [AuditEventType(AuditEventSubType.DataConvert)]
        public async Task<IActionResult> DataConvert([FromBody] Parameters inputParams)
        {
            CheckIfDataConvertIsEnabled();

            ValidateParams(inputParams);

            string inputData = ReadStringParameter(inputParams, DataConvertProperties.InputData);
            string templateCollectionReference = ReadStringParameter(inputParams, DataConvertProperties.TemplateCollectionReference);
            string entryPointTemplate = ReadStringParameter(inputParams, DataConvertProperties.EntryPointTemplate);
            DataConvertInputDataType inputDataType = ReadEnumParameter<DataConvertInputDataType>(inputParams, DataConvertProperties.InputDataType);

            if (!ImageInfo.IsValidImageReference(templateCollectionReference))
            {
                _logger.LogInformation("Templates collection reference format is invalid.");
                throw new RequestNotValidException(string.Format(Resources.InvalidTemplateCollectionReference, templateCollectionReference));
            }

            string registryServer = ExtractRegistryServer(templateCollectionReference);
            var dataConvertRequest = new DataConvertRequest(inputData, inputDataType, registryServer, templateCollectionReference, entryPointTemplate);
            DataConvertResponse response = await _mediator.Send(dataConvertRequest, cancellationToken: default);

            return new ContentResult
            {
                Content = response.Resource,
                ContentType = "application/json",
            };
        }

        /// <summary>
        /// Extract the first component from the image reference in the format of "dockerregistry.io/fedora/httpd:version1.0"
        /// Reference format: https://docs.docker.com/engine/reference/commandline/tag/#extended-description
        /// </summary>
        /// <param name="templateCollectionReference">A string of image reference </param>
        /// <returns>registry server</returns>
        private string ExtractRegistryServer(string templateCollectionReference)
        {
            var referenceComponents = templateCollectionReference.Split(ImageRegistryDelimiter);
            if (referenceComponents.Length <= 1 || string.IsNullOrWhiteSpace(referenceComponents.First()))
            {
                _logger.LogInformation("Templates collection reference is invalid: registry server missing.");
                throw new RequestNotValidException(string.Format(Resources.InvalidTemplateCollectionReference, templateCollectionReference));
            }

            return referenceComponents[0];
        }

        private void ValidateParams(Parameters inputParams)
        {
            if (inputParams == null)
            {
                _logger.LogInformation("Failed to deserialize data convert request body as Parameters resource.");
                throw new RequestNotValidException(Resources.DataConvertParametersNotValid);
            }

            foreach (var param in inputParams.Parameter)
            {
                var paramName = param.Name;
                if (!_supportedParams.Contains(paramName))
                {
                    throw new RequestNotValidException(string.Format(Resources.DataConvertParameterNotValid, paramName));
                }
            }
        }

        private static string ReadStringParameter(Parameters parameters, string paramName)
        {
            var param = parameters?.Parameter.Find(p =>
                string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));

            var paramValue = param?.Value?.ToString();
            if (string.IsNullOrEmpty(paramValue))
            {
                throw new RequestNotValidException(string.Format(Resources.DataConvertParameterValueNotValid, paramName));
            }

            return paramValue;
        }

        private static T ReadEnumParameter<T>(Parameters parameters, string paramName)
        {
            var param = parameters?.Parameter.Find(p =>
                string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));

            object enumValue;
            if (!Enum.TryParse(typeof(T), param?.Value?.ToString(), ignoreCase: true, out enumValue))
            {
                throw new RequestNotValidException(string.Format(Resources.DataConvertParameterValueNotValid, paramName));
            }

            return (T)enumValue;
        }

        private static HashSet<string> GetSupportedParams()
        {
            var supportedParams = new HashSet<string>()
            {
                DataConvertProperties.InputData,
                DataConvertProperties.InputDataType,
                DataConvertProperties.TemplateCollectionReference,
                DataConvertProperties.EntryPointTemplate,
            };

            return supportedParams;
        }

        private void CheckIfDataConvertIsEnabled()
        {
            if (!_config.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.DataConvert));
            }
        }
    }
}
