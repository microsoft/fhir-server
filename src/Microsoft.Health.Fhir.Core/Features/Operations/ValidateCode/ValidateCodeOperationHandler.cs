// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Terminology
{
    public class ValidateCodeOperationHandler : IRequestHandler<ValidateCodeOperationRequest, ValidateCodeOperationResponse>
    {
        public static readonly OperationOutcomeIssue ValidationPassed = new OperationOutcomeIssue(
              OperationOutcomeConstants.IssueSeverity.Information,
              OperationOutcomeConstants.IssueType.Informational,
              Core.Resources.ValidationPassed);

        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ITerminologyOperator _terminologyOperator;

        public ValidateCodeOperationHandler(IAuthorizationService<DataActions> authorizationService, ITerminologyOperator terminologyOperator)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(terminologyOperator, nameof(terminologyOperator));

            _authorizationService = authorizationService;
            _terminologyOperator = terminologyOperator;
        }

        /// <summary>
        /// Handles validation requests.
        /// </summary>
        /// <param name="request">The request</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        public async Task<ValidateCodeOperationResponse> Handle(ValidateCodeOperationRequest request, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.ResourceValidate, cancellationToken) != DataActions.ResourceValidate)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (string.IsNullOrEmpty(request.ResourceID))
            {
                Parameters parameterOutcome = _terminologyOperator.TryValidateCode(request.Resource);
                return new ValidateCodeOperationResponse(parameterOutcome);
            }
            else
            {
                Parameters parameterOutcome = _terminologyOperator.TryValidateCode(request.Resource, request.ResourceID?.ToString(), request.Code?.ToString(), request.System?.ToString(), request.Display?.ToString());
                return new ValidateCodeOperationResponse(parameterOutcome);
            }
        }
    }
}
