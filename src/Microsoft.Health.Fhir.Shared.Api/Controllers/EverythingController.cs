// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    public class EverythingController : Controller
    {
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private static readonly HashSet<string> _supportedParameters = new()
        {
            EverythingOperationParameterNames.Start,
            EverythingOperationParameterNames.End,
            KnownQueryParameterNames.Since,
            KnownQueryParameterNames.Type,
            KnownQueryParameterNames.ContinuationToken,
        };

        public EverythingController(
            IMediator mediator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        /// <summary>
        /// Returns resources defined in $everything operation
        /// </summary>
        /// <param name="idParameter">The resource ID</param>
        [HttpGet]
        [Route(KnownRoutes.PatientEverythingById, Name = RouteNames.PatientEverythingById)]
        [AuditEventType(AuditEventSubType.Everything)]
        public async Task<IActionResult> PatientEverythingById(string idParameter)
        {
            ValidateAndGetParameters(out PartialDateTime start, out PartialDateTime end, out PartialDateTime since, out string type, out string ct, out IReadOnlyList<Tuple<string, string>> unsupportedParameters);

            EverythingOperationResponse result = await _mediator.Send(new EverythingOperationRequest(ResourceType.Patient.ToString(), idParameter, start, end, since, type, ct, unsupportedParameters), HttpContext.RequestAborted);

            return FhirResult.Create(result.Bundle);
        }

        private void ValidateAndGetParameters(
            out PartialDateTime start,
            out PartialDateTime end,
            out PartialDateTime since,
            out string type,
            out string ct,
            out IReadOnlyList<Tuple<string, string>> unsupportedParameters)
        {
            IReadOnlyList<Tuple<string, string>> inputParameters = Request.Query
                .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value))
                .ToArray();

            start = ReadPartialDateTimeParameter(inputParameters, EverythingOperationParameterNames.Start);
            end = ReadPartialDateTimeParameter(inputParameters, EverythingOperationParameterNames.End);
            since = ReadPartialDateTimeParameter(inputParameters, KnownQueryParameterNames.Since);
            type = ReadStringParameter(inputParameters, KnownQueryParameterNames.Type);
            ct = ReadStringParameter(inputParameters, KnownQueryParameterNames.ContinuationToken);
            unsupportedParameters = ReadUnsupportedParameters(inputParameters);
        }

        private static string ReadStringParameter(IReadOnlyList<Tuple<string, string>> inputParameters, string parameterName)
        {
            return inputParameters
                .Where(x => string.Equals(x.Item1, parameterName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Item2)
                .FirstOrDefault();
        }

        private static PartialDateTime ReadPartialDateTimeParameter(IReadOnlyList<Tuple<string, string>> inputParameters, string parameterName)
        {
            string value = ReadStringParameter(inputParameters, parameterName);

            try
            {
                return string.IsNullOrEmpty(value) ? null : PartialDateTime.Parse(value);
            }
            catch
            {
                return null;
            }
        }

        private IReadOnlyList<Tuple<string, string>> ReadUnsupportedParameters(IReadOnlyList<Tuple<string, string>> parameters)
        {
            var unsupportedParameters = parameters.Where(x => !_supportedParameters.Contains(x.Item1.ToLower(CultureInfo.CurrentCulture))).ToList();

            foreach (Tuple<string, string> unsupportedParameter in unsupportedParameters)
            {
                _fhirRequestContextAccessor.RequestContext?.BundleIssues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.NotSupported,
                    string.Format(CultureInfo.InvariantCulture, Resources.UnsupportedParameter, unsupportedParameter.Item1)));
            }

            return unsupportedParameters;
        }
    }
}
