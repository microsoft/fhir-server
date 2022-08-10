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

namespace Microsoft.Health.Fhir.Core.Features.Terminology
{
    public class ExpandOperationHandler : IRequestHandler<ExpandOperationRequest, ExpandOperationResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ITerminologyOperator _terminologyOperator;

        public ExpandOperationHandler(IAuthorizationService<DataActions> authorizationService, ITerminologyOperator terminologyOperator)
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
        public async Task<ExpandOperationResponse> Handle(ExpandOperationRequest request, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.ResourceValidate, cancellationToken) != DataActions.ResourceValidate)
            {
                throw new UnauthorizedFhirActionException();
            }

            Resource parameterOutcome;

            if (request.Parameter != null)
            {
                parameterOutcome = _terminologyOperator.TryExpand(request.Parameter);
            }
            else if (request.ValueSet != null)
            {
                parameterOutcome = _terminologyOperator.TryExpand(request.ValueSet, offset: request.Offset, count: request.Count);
            }
            else
            {
                parameterOutcome = _terminologyOperator.TryExpand(canonicalURL: request.CanonicalURL, offset: request.Offset, count: request.Count);
            }

            return new ExpandOperationResponse(parameterOutcome);
        }
    }
}
