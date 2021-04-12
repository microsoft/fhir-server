// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchHandler : IRequestHandler<MemberMatchRequest, MemberMatchResponse>
    {
        private readonly IMemberMatchService _memberMatchService;

        public MemberMatchHandler(IMemberMatchService memberMatchService)
        {
            EnsureArg.IsNotNull(memberMatchService, nameof(memberMatchService));
            _memberMatchService = memberMatchService;
        }

        public async Task<MemberMatchResponse> Handle(MemberMatchRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            ResourceElement patient = await _memberMatchService.FindMatch(request.Coverage, request.Patient, cancellationToken);
            return new MemberMatchResponse(patient);
        }
    }
}
