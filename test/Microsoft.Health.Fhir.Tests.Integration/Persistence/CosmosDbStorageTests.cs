// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search;
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
        public async Task GivenAContinuationToken_WhenGettingAnId_ThenTheOriginalTokenIsHashed()
        {
            var ct = Guid.NewGuid().ToString();

            var id = await _dataStore.SaveContinuationTokenAsync(ct);

            var result = await _dataStore.GetContinuationTokenAsync(id);

            Assert.Equal(ct, result);
        }

        [Fact]
        public async Task GivenAnEmptyContinuationToken_WhenGettingAnId_ThenAnErrorShouldBeThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _dataStore.SaveContinuationTokenAsync(null));
        }

        [Fact]
        public async Task GivenACorruptContinuationTokenId_WhenGettingAContinuationToken_ThenAnErrorIsThrown()
        {
            var corrupt = Guid.NewGuid().ToString();

            await Assert.ThrowsAnyAsync<InvalidSearchOperationException>(async () => await _dataStore.GetContinuationTokenAsync(corrupt));
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
