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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class DocRefController : Controller
    {
        private readonly IDocRefRequestConverter _converter;
        private readonly USCoreConfiguration _configuration;

        public DocRefController(
            IDocRefRequestConverter converter,
            IOptions<USCoreConfiguration> configuration)
        {
            EnsureArg.IsNotNull(converter, nameof(converter));
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _converter = converter;
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

            var response = await _converter.ConvertAsync(
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

            var parameterList = new List<Tuple<string, string>>(Request.GetQueriesForSearch());
            parameterList.AddRange(ParseParameters(parameters));

            var response = await _converter.ConvertAsync(
                parameterList,
                HttpContext.RequestAborted);
            return FhirResult.Create(response);
        }

        private static string Convert(DataType value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is PrimitiveType)
            {
                return value.ToString();
            }

            if (value is Coding)
            {
                return $"{((Coding)value).System}|{((Coding)value).Code}";
            }

            // NOTE: This won't work for many non-primitive types. We need a way to convert the value of those types to a string correctly.
            return value.ToString();
        }

        private static List<Tuple<string, string>> ParseParameters(Parameters parameters)
        {
            var parameterList = new List<Tuple<string, string>>();
            if (parameters != null)
            {
                parameterList.AddRange(
                    parameters.Parameter
                        .Where(x => !string.IsNullOrEmpty(x.Name))
                        .Select(x => Tuple.Create(x.Name, Convert(x.Value))));
            }

            return parameterList;
        }
    }
}
