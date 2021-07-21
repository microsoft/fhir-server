// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchHandler : IRequestHandler<MemberMatchRequest, MemberMatchResponse>
    {
        private readonly IMemberMatchService _memberMatchService;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public MemberMatchHandler(
            IAuthorizationService<DataActions> authorizationService,
            IMemberMatchService memberMatchService)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(memberMatchService, nameof(memberMatchService));
            _memberMatchService = memberMatchService;
            _authorizationService = authorizationService;
        }

        public async Task<MemberMatchResponse> Handle(MemberMatchRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceElement patient = await _memberMatchService.FindMatch(request.Coverage, request.Patient, cancellationToken);
            return new MemberMatchResponse(patient);
        }
    }
}
