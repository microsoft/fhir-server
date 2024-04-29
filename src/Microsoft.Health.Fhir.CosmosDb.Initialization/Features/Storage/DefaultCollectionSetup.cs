// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage
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

        public Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateFhirCollectionSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
