// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Messages.Operation.Validate;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateOperationHandler : IRequestHandler<ValidateOperationRequest, ValidateOperationResponse>
    {
        public static readonly OperationOutcomeIssue ValidationPassed = new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    Core.Resources.ValidationPassed);

        private readonly IFhirAuthorizationService _authorizationService;

        public ValidateOperationHandler(IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Handles validation requests that produced no errors. All validation is preformed before this is called.
        /// </summary>
        /// <param name="request">The request</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        public async Task<ValidateOperationResponse> Handle(ValidateOperationRequest request, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.ResourceValidate) != DataActions.ResourceValidate)
            {
                throw new UnauthorizedFhirActionException();
            }

            return new ValidateOperationResponse(ValidationPassed);
        }
    }
}
