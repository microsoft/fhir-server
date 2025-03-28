// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage
{
    public interface ICollectionSetup
    {
        public Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken);

        public Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default);

        Task InstallStoredProcs(CancellationToken cancellationToken);

        public Task UpdateFhirCollectionSettingsAsync(CollectionVersion version, CancellationToken cancellationToken);
    }
}
