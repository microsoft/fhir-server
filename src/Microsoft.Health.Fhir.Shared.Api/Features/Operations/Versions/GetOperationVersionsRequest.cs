// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;

namespace Microsoft.Health.Fhir.Api.Operations.Versions
{
    [Operation("$versions", true, new[] { "GET" })]
    public class GetOperationVersionsRequest : IRequest<GetOperationVersionsResponse>, IOperationRequest
    {
        public string AuditEventType => ValueSets.AuditEventSubType.System;
    }
}
