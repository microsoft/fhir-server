// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchHandler : IRequestHandler<MemberMatchRequest, MemberMatchResponse>
    {
        private readonly IMemberMatchService _memberMatchService;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly CoreFeatureConfiguration _coreFeatures;

        public MemberMatchHandler(
            IAuthorizationService<DataActions> authorizationService,
            IMemberMatchService memberMatchService,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(memberMatchService, nameof(memberMatchService));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _memberMatchService = memberMatchService;
            _authorizationService = authorizationService;
            _requestContextAccessor = requestContextAccessor;
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
        }

        public async Task<MemberMatchResponse> HandleAsync(MemberMatchRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckAccess(DataActions.Read, true, cancellationToken);

            if (_coreFeatures.EnableSmartMemberMatchRestriction &&
                _requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
            {
                throw new UnauthorizedFhirActionException();
            }

            ResourceElement patient = await _memberMatchService.FindMatch(request.Coverage, request.Patient, cancellationToken);
            return new MemberMatchResponse(patient);
        }
    }
}
