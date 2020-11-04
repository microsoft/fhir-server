// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Messages.Operation.PatientEverything
{
    [Operation("$everything", false, new[] { "GET" }, DataActions.Read)]
    public class GetPatientEverythingRequest : IRequest<GetPatientEverythingResponse>, IOperationRequest
    {
        public string AuditEventType => AuditEventSubType.Read;
    }
}
