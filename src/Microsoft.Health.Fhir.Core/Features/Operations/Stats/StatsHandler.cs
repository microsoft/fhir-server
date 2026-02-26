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
using Microsoft.Health.Fhir.Core.Messages.Stats;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Stats
{
    /// <summary>
    /// Handles stats requests.
    /// </summary>
    public class StatsHandler : IRequestHandler<StatsRequest, StatsResponse>
    {
        private readonly IStatsProvider _statsProvider;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public StatsHandler(IStatsProvider statsProvider, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(statsProvider, nameof(statsProvider));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _statsProvider = statsProvider;
            _authorizationService = authorizationService;
        }

        public async Task<StatsResponse> Handle(StatsRequest request, CancellationToken cancellationToken)
        {
            // Stats operation requires Search or Read access similar to search operations.
            // We allow DataActions.Read for legacy support.
            var grantedAccess = await _authorizationService.CheckAccess(DataActions.Search | DataActions.Read, cancellationToken);
            if ((grantedAccess & (DataActions.Search | DataActions.Read)) == 0)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await _statsProvider.GetStatsAsync(cancellationToken);
        }
    }
}
