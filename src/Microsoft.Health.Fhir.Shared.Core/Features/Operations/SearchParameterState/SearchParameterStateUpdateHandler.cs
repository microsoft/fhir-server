// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;

namespace Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState
{
    public class SearchParameterStateUpdateHandler : IRequestHandler<UpdateSearchParameterStateRequest, UpdateSearchParameterStateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SearchParameterStateUpdateHandler(IAuthorizationService<DataActions> authorizationService, ISearchParameterDefinitionManager searchParameterDefinitionManager, SearchParameterStatusManager searchParameterStatusManager)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));

            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterStatusManager = searchParameterStatusManager;
        }

        public async Task<UpdateSearchParameterStateResponse> Handle(UpdateSearchParameterStateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }
        }
    }
}
