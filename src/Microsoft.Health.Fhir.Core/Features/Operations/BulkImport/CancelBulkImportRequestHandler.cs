// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    public class CancelBulkImportRequestHandler : IRequestHandler<CancelBulkImportRequest, CancelBulkImportResponse>
    {
        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));

        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly AsyncRetryPolicy _retryPolicy;

        public CancelBulkImportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService)
            : this(fhirOperationDataStore, authorizationService, DefaultRetryCount, DefaultSleepDurationProvider)
        {
        }

        public CancelBulkImportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService authorizationService, int retryCount, Func<int, TimeSpan> sleepDurationProvider)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsGte(retryCount, 0, nameof(retryCount));
            EnsureArg.IsNotNull(sleepDurationProvider, nameof(sleepDurationProvider));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _retryPolicy = Policy.Handle<JobConflictException>()
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<CancelBulkImportResponse> Handle(CancelBulkImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.BulkImport) != DataActions.BulkImport)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                await Task.Delay(1000);
                return new CancelBulkImportResponse(HttpStatusCode.Accepted);
            });
        }
    }
}
