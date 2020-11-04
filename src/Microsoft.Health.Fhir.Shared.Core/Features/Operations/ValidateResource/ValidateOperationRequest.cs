// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Messages.Operation.Validate
{
    [Operation("$validate", true, new[] { "GET", "POST" }, DataActions.ResourceValidate)]
    public class ValidateOperationRequest : IRequest<ValidateOperationResponse>, IOperationRequest
    {
        public ValidateOperationRequest(IOperationRequestContent requestContent)
        {
            EnsureArg.IsNotNull(requestContent, nameof(requestContent));

            Resource = requestContent.Resource;
        }

        public ResourceElement Resource { get; }

        public string AuditEventType => AuditEventSubType.Read;
    }
}
