// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Persistence tests for Stu3
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public partial class FhirStorageTests : IClassFixture<FhirStorageTestsFixture>
    {
        [Fact]
        public async Task GivenStu3Server_WhenUpsertingASavedResourceWithInvalidETagHeader_ThenAPreconditionFailedExceptionIsThrown()
        {
            var saveResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            await Assert.ThrowsAsync<PreconditionFailedException>(async () =>
                await Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId("invalidVersion")));
        }

        [Fact]
        public async Task GivenStu3Server_WhenSoftDeletingWithStaleWeakETag_ThenPreconditionFailedExceptionIsThrown()
        {
            // Arrange
            var createResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var staleETag = WeakETag.FromVersionId(createResult.RawResourceElement.VersionId);

            var updated = Samples.GetJsonSample("WeightInGrams").ToPoco();
            updated.Id = createResult.RawResourceElement.Id;
            await Mediator.UpsertResourceAsync(updated.ToResourceElement());

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() =>
                Mediator.DeleteResourceAsync(
                    new DeleteResourceRequest(
                        new ResourceKey("Observation", createResult.RawResourceElement.Id),
                        DeleteOperation.SoftDelete,
                        weakETag: staleETag)));
        }

        [Fact]
        public async Task GivenStu3Server_WhenSoftDeletingWithStaleWeakETag_ThenResourceIsNotMutatedAfterConflict()
        {
            // Arrange
            var createResult = await Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var staleETag = WeakETag.FromVersionId(createResult.RawResourceElement.VersionId);

            var updated = Samples.GetJsonSample("WeightInGrams").ToPoco();
            updated.Id = createResult.RawResourceElement.Id;
            var updateResult = await Mediator.UpsertResourceAsync(updated.ToResourceElement());
            var currentVersion = updateResult.RawResourceElement.VersionId;

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() =>
                Mediator.DeleteResourceAsync(
                    new DeleteResourceRequest(
                        new ResourceKey("Observation", createResult.RawResourceElement.Id),
                        DeleteOperation.SoftDelete,
                        weakETag: staleETag)));

            // Assert
            var readResult = await Mediator.GetResourceAsync(
                new ResourceKey<Observation>(createResult.RawResourceElement.Id));

            Assert.NotNull(readResult);
            Assert.Equal(currentVersion, readResult.VersionId);
        }
    }
}
