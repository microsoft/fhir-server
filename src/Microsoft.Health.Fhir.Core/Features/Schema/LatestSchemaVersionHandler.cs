// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    internal class LatestSchemaVersionHandler : IRequestHandler<GetCompatibilityVersionRequest, GetCompatibilityVersionResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _fhirAuthorizationService;

        public LatestSchemaVersionHandler(IFhirOperationDataStore fhirOperationDataStore, IFhirAuthorizationService fhirAuthorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(fhirAuthorizationService, nameof(fhirAuthorizationService));
            _fhirOperationDataStore = fhirOperationDataStore;
            _fhirAuthorizationService = fhirAuthorizationService;
        }

        public async Task<GetCompatibilityVersionResponse> Handle(GetCompatibilityVersionRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _fhirAuthorizationService.CheckAccess(DataActions.SchemaMigration) != DataActions.SchemaMigration)
            {
                throw new UnauthorizedFhirActionException();
            }

            int maxCompatibleVersion = await _fhirOperationDataStore.GetLatestCompatibleVersionAsync(request.MaxVersion, cancellationToken);

            return new GetCompatibilityVersionResponse(request.MinVersion, maxCompatibleVersion);
        }
    }
}
