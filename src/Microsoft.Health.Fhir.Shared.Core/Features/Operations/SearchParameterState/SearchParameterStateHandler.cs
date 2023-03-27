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
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;

namespace Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState
{
    public class SearchParameterStateHandler : IRequestHandler<SearchParameterStateRequest, SearchParameterStateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public SearchParameterStateHandler(IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _authorizationService = authorizationService;
        }

        public async Task<SearchParameterStateResponse> Handle(SearchParameterStateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            throw new System.NotImplementedException();
        }
    }
}
