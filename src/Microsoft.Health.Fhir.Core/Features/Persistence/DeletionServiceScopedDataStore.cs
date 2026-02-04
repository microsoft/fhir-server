// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class DeletionServiceScopedDataStore : IDisposable
    {
        private readonly IFhirDataStore _dataStore;
        private readonly IScopeProvider<IFhirDataStore> _dataStoreScopeProvider;

        private IScoped<IFhirDataStore> _scopedDataStore;

        public DeletionServiceScopedDataStore(IFhirDataStore dataStore)
        {
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
        }

        public DeletionServiceScopedDataStore(IScopeProvider<IFhirDataStore> dataStoreScopeProvider)
        {
            _dataStoreScopeProvider = EnsureArg.IsNotNull(dataStoreScopeProvider, nameof(dataStoreScopeProvider));
        }

        public IFhirDataStore GetDataStore()
        {
            // If we already have a non-scoped data store, return it.
            // This is a valid scenario for services that do not require scoped dependencies, like Gen2.
            if (_dataStore != null)
            {
                return _dataStore;
            }
            else
            {
                // If we have already created the scoped data store, return it.
                // This is a valid scenario for services that require scoped dependencies, like Gen1.
                if (_scopedDataStore != null)
                {
                    return _scopedDataStore.Value;
                }
                else
                {
                    // Create the scoped data store.
                    _scopedDataStore = _dataStoreScopeProvider.Invoke();

                    return _scopedDataStore.Value;
                }
            }
        }

        public void Dispose()
        {
            if (_scopedDataStore != null)
            {
                _scopedDataStore.Dispose();
                _scopedDataStore = null;
            }

            // Not disposing _dataStore and _dataStoreScopeProvider because we don't own its lifetime.
            // Only instances of IScoped<T> created via IScopeProvider<T> are disposed here.
        }
    }
}
