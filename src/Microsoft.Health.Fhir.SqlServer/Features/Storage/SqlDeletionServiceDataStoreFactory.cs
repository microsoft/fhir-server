// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public sealed class SqlDeletionServiceDataStoreFactory : IDeletionServiceDataStoreFactory
    {
        private readonly IFhirDataStore _dataStore;

        public SqlDeletionServiceDataStoreFactory(IFhirDataStore dataStore)
        {
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
        }

        public DeletionServiceScopedDataStore GetScopedDataStore()
        {
            return new DeletionServiceScopedDataStore(_dataStore);
        }
    }
}
