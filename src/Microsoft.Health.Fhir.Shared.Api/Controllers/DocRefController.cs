// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Azure.Core;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class DocRefController : Controller
    {
        private readonly IDocRefRequestProcessor _processor;
        private readonly USCoreConfiguration _configuration;

        public DocRefController(
            IDocRefRequestProcessor processor,
            IOptions<USCoreConfiguration> configuration)
        {
            EnsureArg.IsNotNull(processor, nameof(processor));
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _processor = processor;
            _configuration = configuration.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.DocRefResourceType, Name = RouteNames.DocRef)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
        public async Task<IActionResult> Search()
        {
            if (!_configuration.EnableDocRef)
            {
                throw new RequestNotValidException(
                    string.Format(Resources.OperationNotEnabled, OperationsConstants.DocRef));
            }

            var response = await _processor.ProcessAsync(
                Request.GetQueriesForSearch(),
                HttpContext.RequestAborted);
            return FhirResult.Create(response);
        }

        [HttpPost]
        [Route(KnownRoutes.DocRefResourceType, Name = RouteNames.DocRef)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
        public async Task<IActionResult> Search([FromBody] Parameters parameters)
        {
            if (!_configuration.EnableDocRef)
            {
                throw new RequestNotValidException(
                    string.Format(Resources.OperationNotEnabled, OperationsConstants.DocRef));
            }

            var response = await _processor.ProcessAsync(
                ParseParameters(parameters),
                HttpContext.RequestAborted);
            return FhirResult.Create(response);
        }

        private static List<Tuple<string, string>> ParseParameters(Parameters parameters)
        {
            var parameterList = new List<Tuple<string, string>>();
            if (parameters != null)
            {
                foreach (var p in parameters.Parameter)
                {
                    if (!string.IsNullOrEmpty(p.Name))
                    {
                        parameterList.Add(Tuple.Create(p.Name, p.Value?.ToString()));
                    }
                }
            }

            return parameterList;
        }
    }
}
