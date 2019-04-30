// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class FhirStorageTestsFixture : IDisposable
    {
        private readonly IScoped<IFhirDataStore> _scopedStore;
        private readonly IScoped<IFhirOperationDataStore> _scopedFhirOperationDataStore;
        private readonly IScoped<IFhirStorageTestHelper> _scopedTestHelper;

        public FhirStorageTestsFixture(DataStore dataStore)
        {
            switch (dataStore)
            {
                case Common.FixtureParameters.DataStore.CosmosDb:
                    var fixture = new CosmosDbFhirStorageTestsFixture();

                    _scopedStore = fixture;
                    _scopedFhirOperationDataStore = fixture;
                    _scopedTestHelper = fixture;
                    break;
                case Common.FixtureParameters.DataStore.Sql:
                    throw new NotSupportedException();
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataStore), dataStore, null);
            }
        }

        public IFhirDataStore DataStore => _scopedStore.Value;

        public IFhirOperationDataStore OperationDataStore => _scopedFhirOperationDataStore.Value;

        public IFhirStorageTestHelper TestHelper => _scopedTestHelper.Value;

        public void Dispose()
        {
            _scopedStore?.Dispose();
            _scopedFhirOperationDataStore?.Dispose();
            _scopedTestHelper?.Dispose();
        }
    }
}
