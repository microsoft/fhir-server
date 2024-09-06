// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage
{
    /// <summary>
    /// This class does not execute any real initialization.
    /// It's used as a placeholder to external initialization
    /// </summary>
    public class DefaultCollectionSetup : ICollectionSetup
    {
        public Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task InstallStoredProcs(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateFhirCollectionSettingsAsync(CollectionVersion version, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
