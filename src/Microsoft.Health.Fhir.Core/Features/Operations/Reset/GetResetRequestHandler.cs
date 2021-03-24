// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reset.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reset;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reset
{
    public class GetResetRequestHandler : IRequestHandler<GetResetRequest, GetResetResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public GetResetRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetResetResponse> Handle(GetResetRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reset) != DataActions.Reset)
            {
                throw new UnauthorizedFhirActionException();
            }

            GetResetResponse resetResponse;

            // fake result
            var dateTimeOffset = new DateTimeOffset(new DateTime(2021, 1, 19, 7, 0, 0), TimeSpan.Zero);
            var jobResult = new ResetJobResult(
                dateTimeOffset,
                new Uri("https://localhost/123"),
                new List<ResetOutputResponse>());
            resetResponse = new GetResetResponse(HttpStatusCode.OK, jobResult);

            return resetResponse;
        }
    }
}
