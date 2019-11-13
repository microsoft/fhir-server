// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateOperationHandler : IRequestHandler<ValidateOperationRequest, ValidateOperationResponse>
    {
        public Task<ValidateOperationResponse> Handle(ValidateOperationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ValidateOperationResponse(
                new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    "OK")));
        }
    }
}
