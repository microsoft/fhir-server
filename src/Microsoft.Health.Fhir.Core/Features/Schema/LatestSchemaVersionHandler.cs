// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Schema;
using Microsoft.Health.Fhir.Core.Messages.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    internal class LatestSchemaVersionHandler : IRequestHandler<GetCompatibilityVersionRequest, GetCompatibilityVersionResponse>
    {
        private readonly ISchemaMigrationDataStore _schemaMigrationDataStore;

        public LatestSchemaVersionHandler(ISchemaMigrationDataStore schemaMigrationDataStore)
        {
            EnsureArg.IsNotNull(schemaMigrationDataStore, nameof(schemaMigrationDataStore));
            _schemaMigrationDataStore = schemaMigrationDataStore;
        }

        public async Task<GetCompatibilityVersionResponse> Handle(GetCompatibilityVersionRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            int maxCompatibleVersion = await _schemaMigrationDataStore.GetLatestCompatibleVersionAsync(request.MaxVersion, cancellationToken);

            return new GetCompatibilityVersionResponse(request.MinVersion, maxCompatibleVersion);
        }
    }
}
