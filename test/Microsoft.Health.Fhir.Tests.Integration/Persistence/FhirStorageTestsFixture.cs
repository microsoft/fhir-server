// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class FhirStorageTestsFixture : IAsyncLifetime, IDisposable
    {
        private readonly IServiceProvider _fixture;

        public FhirStorageTestsFixture(DataStore dataStore)
        {
            switch (dataStore)
            {
                case Common.FixtureParameters.DataStore.CosmosDb:
                    _fixture = new CosmosDbFhirStorageTestsFixture();
                    break;
                case Common.FixtureParameters.DataStore.SqlServer:
                    _fixture = new SqlServerFhirStorageTestsFixture();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataStore), dataStore, null);
            }
        }

        public IFhirDataStore DataStore => _fixture.GetRequiredService<IFhirDataStore>();

        public IFhirOperationDataStore OperationDataStore => _fixture.GetRequiredService<IFhirOperationDataStore>();

        public IFhirStorageTestHelper TestHelper => _fixture.GetRequiredService<IFhirStorageTestHelper>();

        void IDisposable.Dispose()
        {
            (_fixture as IDisposable)?.Dispose();
        }

        async Task IAsyncLifetime.InitializeAsync()
        {
            if (_fixture is IAsyncLifetime asyncLifetime)
            {
                await asyncLifetime.InitializeAsync();
            }
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            if (_fixture is IAsyncLifetime asyncLifetime)
            {
                await asyncLifetime.DisposeAsync();
            }
        }
    }
}
