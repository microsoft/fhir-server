// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ValidateModelState]
    [ValidateParametersResourceAttribute]
    public class MemberMatchController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ILogger _logger;

        public const string Patient = "MemberPatient";
        public const string Coverage = "OldCoverage";

        public MemberMatchController(IMediator mediator, ILogger<MemberMatchController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.MemberMatch)]
        [AuditEventType(AuditEventSubType.MemberMatch)]
        public async Task<IActionResult> MemberMatch([FromBody] Parameters inputParams)
        {
            ValidateParams(inputParams, out ResourceElement coverage, out ResourceElement patient);

            var response = await _mediator.Send<MemberMatchResponse>(new MemberMatchRequest(coverage, patient), HttpContext.RequestAborted);
            var parameters = new Parameters();
            parameters.Add(Patient, response.Patient.ToPoco<Patient>());
            return MemberMatchResult.Ok(parameters);
        }

        private void ValidateParams(Parameters inputParams, out ResourceElement coverage, out ResourceElement patient)
        {
            if (inputParams == null)
            {
                _logger.LogInformation("Failed to deserialize member-match request body as Parameters resource.");
                throw new RequestNotValidException(Resources.MemberMatchInvalidParameter);
            }

            var coverageResource = inputParams.GetSingle(Coverage)?.Resource;
            if (coverageResource == null)
            {
                throw new RequestNotValidException(Resources.MemberMatchOldCoverageNotFound);
            }

            coverage = coverageResource.ToResourceElement();

            var patientResource = inputParams.GetSingle(Patient)?.Resource;
            if (patientResource == null)
            {
                throw new RequestNotValidException(Resources.MemberMatchMemberPatientNotFound);
            }

            patient = patientResource.ToResourceElement();
        }
    }
}
