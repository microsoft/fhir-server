// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
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
        private static Dictionary<string, HashSet<string>> _supportedParams = InitSupportedParams();

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

            string inputData = ReadStringParameter(inputParams, JobRecordProperties.InputData);
            string templateSetReference = ReadStringParameter(inputParams, JobRecordProperties.TemplateSetReference);
            string entryPointTemplate = ReadStringParameter(inputParams, JobRecordProperties.EntryPointTemplate);
            DataConvertInputDataType inputDataType = ReadEnumParameter<DataConvertInputDataType>(inputParams, JobRecordProperties.InputDataType);

            var dataConvertRequest = new DataConvertRequest(inputData, inputDataType, templateSetReference, entryPointTemplate);
            DataConvertResponse response = await _mediator.Send(dataConvertRequest, cancellationToken: default);

            return new ContentResult
            {
                Content = response.Resource,
                ContentType = "application/json",
            };
        }

        private void ValidateParams(Parameters inputParams)
        {
            if (inputParams == null)
            {
                _logger.LogInformation("Failed to deserialize data convert request body as Parameters resource.");
                throw new RequestNotValidException(Resources.DataConvertParametersNotValid);
            }

            var supportedParams = _supportedParams[Request.Method];

            foreach (var param in inputParams.Parameter)
            {
                var paramName = param.Name;
                if (!supportedParams.Contains(paramName))
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

        private static Dictionary<string, HashSet<string>> InitSupportedParams()
        {
            var postParams = new HashSet<string>()
            {
                JobRecordProperties.InputData,
                JobRecordProperties.InputDataType,
                JobRecordProperties.TemplateSetReference,
                JobRecordProperties.EntryPointTemplate,
            };

            var patchParams = new HashSet<string>()
            {
                JobRecordProperties.MaximumConcurrency,
                JobRecordProperties.Status,
            };

            var supportedParams = new Dictionary<string, HashSet<string>>();
            supportedParams.Add(HttpMethods.Post, postParams);

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
