// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public partial class FhirStorageTests : IClassFixture<FhirStorageTestsFixture>
    {
        [Fact]
        public async Task WhenUpsertingASavedResourceWithInvalidETagHeader_GivenR4Server_ThenPreconditionFailedIsThrown()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.Resource.Id;

            await Assert.ThrowsAsync<PreconditionFailedException>(async () =>
                await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task WhenUpsertingANonexistentResourceWithCreateDisabledAndInvalidETagHeader_GivenR4ServerAndSqlServer_ThenPreconditionFailedIsThrown()
        {
            var observation = _conformance.Rest[0].Resource.Find(r => r.Type == ResourceType.Observation);
            observation.UpdateCreate = false;
            observation.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;
            await Assert.ThrowsAsync<PreconditionFailedException>(() => Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task WhenUpsertingANonexistentResourceWithInvalidETagHeader_GivenR4ServerAndCosmosDb_ThenResourceNotFoundIsThrown()
        {
            Resource newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = Guid.NewGuid().ToString();

            await Assert.ThrowsAsync<ResourceNotFoundException>(async () =>
                await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId("incorrectVersion")));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task WhenUpsertingANonexistentResourceWithInvalidETagHeader_GivenR4ServerAndSqlServer_ThenPreconditionFailedIsThrown()
        {
            Resource newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = Guid.NewGuid().ToString();

            await Assert.ThrowsAsync<PreconditionFailedException>(async () =>
                await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId("incorrectVersion")));
        }
    }
}
