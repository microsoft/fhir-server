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
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    public class GetBulkImportRequestHandler : IRequestHandler<GetBulkImportRequest, GetBulkImportResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;

        public GetBulkImportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetBulkImportResponse> Handle(GetBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.BulkImport) != DataActions.BulkImport)
            {
                throw new UnauthorizedFhirActionException();
            }

            GetBulkImportResponse bulkImportResponse;

            // fake result
            var dateTimeOffset = new DateTimeOffset(new DateTime(2021, 1, 19, 7, 0, 0), TimeSpan.Zero);
            var jobResult = new BulkImportJobResult(
                dateTimeOffset,
                new Uri("https://localhost/123"),
                new List<BulkImportOutputResponse>(),
                new List<BulkImportOutputResponse>());
            bulkImportResponse = new GetBulkImportResponse(HttpStatusCode.OK, jobResult);

            return bulkImportResponse;
        }
    }
}
