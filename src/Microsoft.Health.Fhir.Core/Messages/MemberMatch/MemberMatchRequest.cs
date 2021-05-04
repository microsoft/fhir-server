// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.MemberMatch
{
    public sealed class MemberMatchRequest : IRequest<MemberMatchResponse>, IRequest
    {
        public MemberMatchRequest(ResourceElement coverage, ResourceElement patient)
        {
            Coverage = coverage;
            Patient = patient;
        }

        public ResourceElement Coverage { get; }

        public ResourceElement Patient { get; }
    }
}
