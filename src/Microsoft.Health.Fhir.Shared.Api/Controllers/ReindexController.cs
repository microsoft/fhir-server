// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.AspNetCore.Http;
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
        private readonly ReindexJobConfiguration _config;
        private readonly ILogger<ReindexController> _logger;
        private static Dictionary<string, HashSet<string>> _supportedParams = InitSupportedParams();
        private readonly IUrlResolver _urlResolver;

        public ReindexController(
            IMediator mediator,
            IOptions<OperationsConfiguration> operationsConfig,
            IUrlResolver urlResolver,
            ILogger<ReindexController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Reindex, nameof(operationsConfig));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _config = operationsConfig.Value.Reindex;
            _urlResolver = urlResolver;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.Reindex)]
        [ServiceFilter(typeof(ValidateReindexRequestFilterAttribute))]
        [ServiceFilter(typeof(ValidateParametersResourceAttribute))]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> CreateReindexJob([FromBody] Parameters inputParams)
        {
            CheckIfReindexIsEnabledAndRespond();

            ValidateParams(inputParams);

            uint? maxResourcesPerQuery = (uint?)ReadNumericParameter(inputParams, JobRecordProperties.MaximumNumberOfResourcesPerQuery);
            uint? maxResourcesPerWrite = (uint?)ReadNumericParameter(inputParams, JobRecordProperties.MaximumNumberOfResourcesPerWrite);
            int? queryDelay = ReadNumericParameter(inputParams, JobRecordProperties.QueryDelayIntervalInMilliseconds);
            ushort? targetDataStoreResourcePercentage = (ushort?)ReadNumericParameter(inputParams, JobRecordProperties.TargetDataStoreUsagePercentage);
            string targetResourceTypes = ReadStringParameter(inputParams, JobRecordProperties.TargetResourceTypes);
            string targetSearchParamTypes = ReadStringParameter(inputParams, JobRecordProperties.TargetSearchParameterTypes);

            ResourceElement response = await _mediator.CreateReindexJobAsync(
                maxResourcesPerQuery,
                maxResourcesPerWrite,
                queryDelay,
                targetDataStoreResourcePercentage,
                targetResourceTypes,
                targetSearchParamTypes,
                HttpContext.RequestAborted);

            var result = FhirResult.Create(response, HttpStatusCode.Created)
                .SetETagHeader()
                .SetLastModifiedHeader();

            result.SetContentLocationHeader(_urlResolver, OperationsConstants.Reindex, response.Id);
            return result;
        }

        [HttpPost]
        [HttpGet]
        [Route(KnownRoutes.ReindexSingleResource)]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> ReindexSingleResource(string typeParameter, string idParameter)
        {
            CheckIfReindexIsEnabledAndRespond();

            ReindexSingleResourceResponse response = await _mediator.SendReindexSingleResourceRequestAsync(Request.Method, typeParameter, idParameter, HttpContext.RequestAborted);

            var result = FhirResult.Create(response.ParameterResource, HttpStatusCode.OK);

            return result;
        }

        [HttpGet]
        [Route(KnownRoutes.Reindex)]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> ListReindexJobs()
        {
            CheckIfReindexIsEnabledAndRespond();

            ResourceElement response = await _mediator.GetReindexJobAsync(null, HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.OK)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        [HttpGet]
        [Route(KnownRoutes.ReindexJobLocation, Name = RouteNames.GetReindexStatusById)]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> GetReindexJob(string idParameter)
        {
            CheckIfReindexIsEnabledAndRespond();

            ResourceElement response = await _mediator.GetReindexJobAsync(idParameter, HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.OK)
                .SetETagHeader()
                .SetLastModifiedHeader();
        }

        [HttpDelete]
        [Route(KnownRoutes.ReindexJobLocation)]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> CancelReindex(string idParameter)
        {
            CheckIfReindexIsEnabledAndRespond();

            ResourceElement response = await _mediator.CancelReindexAsync(idParameter, HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.Accepted)
                .SetETagHeader()
                .SetLastModifiedHeader();
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

        private void ValidateParams(Parameters inputParams)
        {
            if (inputParams == null)
            {
                _logger.LogInformation("Failed to deserialize reindex job request body as Parameters resource.");
                return;
            }

            var supportedParams = _supportedParams[Request.Method];

            foreach (var param in inputParams.Parameter)
            {
                var paramName = param.Name;
                _logger.LogInformation(string.Format("Reindex job received parameter {0} for method {1}", SanitizeStringForLogging(paramName), SanitizeStringForLogging(Request.Method)));

                if (!supportedParams.Contains(paramName))
                {
                    _logger.LogInformation(string.Format(Resources.ReindexParameterNotValid, SanitizeStringForLogging(paramName), SanitizeStringForLogging(Request.Method)));
                }
            }
        }

        private int? ReadNumericParameter(Parameters parameters, string paramName)
        {
            var param = parameters?.Parameter.Find(p =>
                string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));

            if (param == null)
            {
                return null;
            }

            if (int.TryParse(param.Value.ToString(), out var intValue))
            {
                return intValue;
            }
            else
            {
                throw new RequestNotValidException(string.Format(Resources.ReindexParameterNotValid, paramName, Request.Method));
            }
        }

        private static string ReadStringParameter(Parameters parameters, string paramName)
        {
            var param = parameters?.Parameter.Find(p =>
                string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase));

            if (param == null)
            {
                return null;
            }

            return param.Value?.ToString();
        }

        private static Dictionary<string, HashSet<string>> InitSupportedParams()
        {
            var postParams = new HashSet<string>()
            {
                JobRecordProperties.MaximumNumberOfResourcesPerQuery,
                JobRecordProperties.MaximumNumberOfResourcesPerWrite,
            };

            var patchParams = new HashSet<string>()
            {
                JobRecordProperties.MaximumNumberOfResourcesPerQuery,
                JobRecordProperties.MaximumNumberOfResourcesPerWrite,
                JobRecordProperties.Status,
            };

            var supportedParams = new Dictionary<string, HashSet<string>>();
            supportedParams.Add(HttpMethods.Post, postParams);
            supportedParams.Add(HttpMethods.Patch, patchParams);

            return supportedParams;
        }

        /// <summary>
        /// Sanitizes a string for safe logging by removing newlines, carriage returns, tabs, and other control characters
        /// that could be used for log injection attacks (CRLF injection, log forging).
        /// </summary>
        private static string SanitizeStringForLogging(string input)
        {
            if (input == null)
            {
                return "[null]";
            }

            if (input.Length == 0)
            {
                return "[empty]";
            }

            // Remove all control characters (ASCII < 32 or == 127), but preserve spaces and printable characters
            var sanitized = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (!char.IsControl(c))
                {
                    sanitized.Append(c);
                }
            }

            return sanitized.ToString();
        }
    }
}
