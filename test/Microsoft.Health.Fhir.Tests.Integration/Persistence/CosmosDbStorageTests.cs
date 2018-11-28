// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbStorageTests : FhirStorageTestsBase, IClassFixture<IntegrationTestCosmosDataStore>
    {
        private readonly IntegrationTestCosmosDataStore _dataStore;

        public CosmosDbStorageTests(IntegrationTestCosmosDataStore dataStore)
            : base(dataStore)
        {
            _dataStore = dataStore;
        }

        [Fact]
        public async Task GivenANewResource_WhenUpserting_ThenTheVersionStartsAt1()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            Assert.Equal("1", saveResult.Resource.Meta.VersionId);

            saveResult = await FhirRepository.UpsertAsync(saveResult.Resource);

            Assert.Equal("2", saveResult.Resource.Meta.VersionId);
        }
    }
}
