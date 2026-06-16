// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Conformance;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class TerminologyController : Controller
    {
        private static readonly HashSet<string> ExpandParameterNames = new HashSet<string>(
            TerminologyOperationParameterNames.Expand.Names,
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExpandParameterNamesMultipleAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            TerminologyOperationParameterNames.Expand.Designation,
            TerminologyOperationParameterNames.Expand.ExcludeSystem,
            TerminologyOperationParameterNames.Expand.SystemVersion,
            TerminologyOperationParameterNames.Expand.CheckSystemVersion,
            TerminologyOperationParameterNames.Expand.ForceSystemVersion,
        };

        private readonly IMediator _mediator;
        private readonly TerminologyConfiguration _configuration;

        public TerminologyController(
            IMediator mediator,
            IOptions<TerminologyConfiguration> configuration)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _mediator = mediator;
            _configuration = configuration.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.ExpandResourceType, Name = RouteNames.Expand)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<IActionResult> Expand()
        {
            if (!_configuration.EnableExpand)
            {
                throw new RequestNotValidException(
                    string.Format(Resources.OperationNotEnabled, OperationsConstants.ValueSetExpand));
            }

            var parameters = Request.GetQueriesForSearch();
            ValidateExpandParameters(parameters);

            var request = new ExpandRequest(parameters);
            var response = await _mediator.Send<ExpandResponse>(
                request,
                HttpContext.RequestAborted);
            return FhirResult.Create(response.Resource);
        }

        [HttpGet]
        [Route(KnownRoutes.ExpandResourceId, Name = RouteNames.ExpandById)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<IActionResult> Expand([FromRoute] string idParameter)
        {
            if (!_configuration.EnableExpand)
            {
                throw new RequestNotValidException(
                    string.Format(Resources.OperationNotEnabled, OperationsConstants.ValueSetExpand));
            }

            if (string.IsNullOrEmpty(idParameter))
            {
                throw new RequestNotValidException(Resources.ExpandInvalidResourceId);
            }

            var parameters = Request.GetQueriesForSearch();
            ValidateExpandParameters(parameters, idParameter);

            var request = new ExpandRequest(parameters, idParameter);
            var response = await _mediator.Send<ExpandResponse>(
                request,
                HttpContext.RequestAborted);
            return FhirResult.Create(response.Resource);
        }

        [HttpPost]
        [Route(KnownRoutes.ExpandResourceType, Name = RouteNames.Expand)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<IActionResult> Expand([FromBody] Parameters parameters)
        {
            if (!_configuration.EnableExpand)
            {
                throw new RequestNotValidException(
                    string.Format(Resources.OperationNotEnabled, OperationsConstants.ValueSetExpand));
            }

            var parameterList = new List<Tuple<string, string>>(Request.GetQueriesForSearch());
            parameterList.AddRange(ParseParameters(parameters));
            ValidateExpandParameters(parameterList);

            var request = new ExpandRequest(parameterList);
            var response = await _mediator.Send<ExpandResponse>(
                request,
                HttpContext.RequestAborted);
            return FhirResult.Create(response.Resource);
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
                        .Select(x => Tuple.Create(x.Name, x.Resource != null ? x.Resource.ToJson() : Convert(x.Value))));
            }

            return parameterList;
        }

        private static void ValidateExpandParameters(
            IReadOnlyList<Tuple<string, string>> parameters,
            string resourceId = default)
        {
            // Note: this validation is based on: https://hl7.org/fhir/R4/valueset-operation-expand.html
            // One of these parameters "url", "valueSet", or "context" must be provided for a non-instance level operation.
            if (string.IsNullOrEmpty(resourceId)
                && (parameters == null
                || (!parameters.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Url, StringComparison.OrdinalIgnoreCase))
                && !parameters.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase))
                && !parameters.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Context, StringComparison.OrdinalIgnoreCase)))))
            {
                throw new RequestNotValidException(Resources.ExpandMissingRequiredParameter);
            }

            if (parameters != null && parameters.Any())
            {
                var invalid = parameters
                    .Where(x => !ExpandParameterNames.Contains(x.Item1))
                    .Select(x => x.Item1)
                    .ToList();
                if (invalid.Any())
                {
                    StringBuilder s = new StringBuilder();
                    foreach (var i in invalid)
                    {
                        s.AppendFormat("'{0}',", i);
                    }

                    throw new RequestNotValidException(
                        string.Format(Resources.ExpandInvalidParameter, s.ToString().TrimEnd(',')));
                }

                var invalidCount = parameters
                    .GroupBy(x => x.Item1)
                    .Where(x => x.Count() > 1 && !ExpandParameterNamesMultipleAllowed.Contains(x.Key))
                    .Select(x => x.Key)
                    .ToList();
                if (!string.IsNullOrEmpty(resourceId)
                    && parameters.Any(x => string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase)))
                {
                    invalidCount.Add(TerminologyOperationParameterNames.Expand.ValueSet);
                }

                if (invalidCount.Any())
                {
                    StringBuilder s = new StringBuilder();
                    foreach (var i in invalidCount)
                    {
                        s.AppendFormat("'{0}',", i);
                    }

                    throw new RequestNotValidException(
                        string.Format(Resources.ExpandInvalidParameterCount, s.ToString().TrimEnd(',')));
                }
            }
        }
    }
}
