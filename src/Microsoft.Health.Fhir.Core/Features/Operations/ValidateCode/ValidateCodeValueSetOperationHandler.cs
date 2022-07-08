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

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateCodeValueSetOperationHandler : IRequestHandler<ValidateCodeValueSetOperationRequest, ValidateCodeValueSetOperationResponse>
    {
        public static readonly OperationOutcomeIssue ValidationPassed = new OperationOutcomeIssue(
              OperationOutcomeConstants.IssueSeverity.Information,
              OperationOutcomeConstants.IssueType.Informational,
              Core.Resources.ValidationPassed);

        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IProfileValidator _profileValidator;

        public ValidateCodeValueSetOperationHandler(IAuthorizationService<DataActions> authorizationService, IProfileValidator profileValidator)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(profileValidator, nameof(profileValidator));

            _authorizationService = authorizationService;
            _profileValidator = profileValidator;
        }

        /// <summary>
        /// Handles validation requests.
        /// </summary>
        /// <param name="request">The request</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        public async Task<ValidateCodeValueSetOperationResponse> Handle(ValidateCodeValueSetOperationRequest request, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.ResourceValidate, cancellationToken) != DataActions.ResourceValidate)
            {
                throw new UnauthorizedFhirActionException();
            }

            Parameters parameterOutcome = _profileValidator.TryValidateCodeValueSet(request.Resource, request.ValueSetID, request.System?.ToString(), request.Code?.ToString(), request.Display?.ToString());
            return new ValidateCodeValueSetOperationResponse(parameterOutcome);
        }
    }
}
