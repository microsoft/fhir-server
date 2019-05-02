// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class FhirStorageTestsFixture : IDisposable
    {
        private readonly IScoped<IFhirDataStore> _scopedStore;

        public FhirStorageTestsFixture(DataStore dataStore)
        {
            switch (dataStore)
            {
                case Common.FixtureParameters.DataStore.CosmosDb:
                    _scopedStore = new CosmosDbFhirStorageTestsFixture();
                    break;
                case Common.FixtureParameters.DataStore.Sql:
                    _scopedStore = new SqlServerFhirStorageTestsFixture();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataStore), dataStore, null);
            }
        }

        public IFhirDataStore DataStore => _scopedStore.Value;

        public void Dispose()
        {
            _scopedStore?.Dispose();
        }
    }
}
