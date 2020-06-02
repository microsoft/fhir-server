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
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class ReindexController : Controller
    {
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ReindexJobConfiguration _config;
        private readonly ILogger<ReindexController> _logger;

        public ReindexController(
            IMediator mediator,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<ReindexController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Reindex, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _config = operationsConfig.Value.Reindex;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.Reindex)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Reindex)]
        public async Task<IActionResult> CreateReindexJob([FromBody] string body)
        {
            var parameters = ValidateBody(body);

            int? maximumConcurrency = ReadNumericParameter(parameters, ReindexJobParameters.MaximumConcurrency);
            string scope = ReadStringParameter(parameters, ReindexJobParameters.Scope);

            ResourceElement response = await _mediator.CreateReindexJobAsync(maximumConcurrency, scope, HttpContext.RequestAborted);

            return FhirResult.Create(response, HttpStatusCode.Created)
                .SetETagHeader()
                .SetLastModifiedHeader();
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

        private Parameters ValidateBody(string body)
        {
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var parameters = JsonConvert.DeserializeObject<Parameters>(body);

                    var supportedParams = new HashSet<string>();
                    if (HttpMethods.IsPost(Request.Method))
                    {
                        supportedParams.Add(ReindexJobParameters.MaximumConcurrency);
                        supportedParams.Add(ReindexJobParameters.Scope);
                    }
                    else if (HttpMethods.IsPost(Request.Method))
                    {
                        supportedParams.Add(ReindexJobParameters.MaximumConcurrency);
                        supportedParams.Add(ReindexJobParameters.Status);
                    }

                    foreach (var param in parameters.Parameter)
                    {
                        var paramName = param.Name;
                        if (!supportedParams.Contains(paramName))
                        {
                            throw new RequestNotValidException(string.Format(Resources.ReindexParameterNotValid, paramName, Request.Method));
                        }
                    }

                    return parameters;
                }
                catch (JsonException jex)
                {
                    _logger.LogInformation("Failed to deserialize reindex job request body as Parameters resource. Error: {0}", jex.Message);
                    throw new RequestNotValidException(Resources.ReindexParametersNotValid);
                }
            }
            else
            {
                return null;
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
    }
}
