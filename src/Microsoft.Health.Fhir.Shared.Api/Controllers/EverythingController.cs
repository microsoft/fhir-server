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
        /// <param name="start">The start date relates to care dates</param>
        /// <param name="end">The end date relates to care dates</param>
        /// <param name="since">Resources that have been updated since this time will be included in the response</param>
        /// <param name="type">Comma-delimited FHIR resource types to include in the return resources</param>
        /// <param name="ct">The continuation token</param>
        [HttpGet]
        [Route(KnownRoutes.PatientEverythingById, Name = RouteNames.PatientEverythingById)]
        [AuditEventType(AuditEventSubType.Everything)]
        public async Task<IActionResult> PatientEverythingById(
            string idParameter,
            [FromQuery(Name = EverythingOperationParameterNames.Start)] PartialDateTime start,
            [FromQuery(Name = EverythingOperationParameterNames.End)] PartialDateTime end,
            [FromQuery(Name = KnownQueryParameterNames.Since)] PartialDateTime since,
            [FromQuery(Name = KnownQueryParameterNames.Type)] string type,
            string ct)
        {
            IReadOnlyList<Tuple<string, string>> unsupportedParameters = ReadUnsupportedParameters();

            EverythingOperationResponse result = await _mediator.Send(new EverythingOperationRequest(ResourceType.Patient.ToString(), idParameter, start, end, since, type, ct, unsupportedParameters), HttpContext.RequestAborted);

            return FhirResult.Create(result.Bundle);
        }

        private List<Tuple<string, string>> ReadUnsupportedParameters()
        {
            IReadOnlyList<Tuple<string, string>> parameters = Request.Query
                .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value))
                .ToArray();

            var unsupportedParameters = parameters.Where(x => !_supportedParameters.Contains(x.Item1)).ToList();

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
