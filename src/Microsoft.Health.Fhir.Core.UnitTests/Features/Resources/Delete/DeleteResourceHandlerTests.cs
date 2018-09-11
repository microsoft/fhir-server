// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Delete
{
    public class DeleteResourceHandlerTests
    {
        [Fact]
        public async Task GivenADeleteResourceRequest_WhenHandled_ThenTheResponseShouldHaveTheDeletedVersion()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new DeleteResourceHandler(repository);
            var resourceId = Guid.NewGuid().ToString();
            var deletedResourceKey = new ResourceKey("Observation", resourceId, Guid.NewGuid().ToString());

            repository.DeleteAsync(Arg.Any<ResourceKey>(), false).Returns(deletedResourceKey);

            DeleteResourceResponse response = await handler.Handle(
                new DeleteResourceRequest("Observation", resourceId, false),
                CancellationToken.None);

            await repository.Received().DeleteAsync(Arg.Any<ResourceKey>(), false);

            Assert.Equal(deletedResourceKey.VersionId, response.WeakETag.VersionId);
        }

        [Fact]
        public async Task GivenAHardDeleteResourceRequest_WhenHandled_ThenTheResponseShouldNotHaveVersion()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new DeleteResourceHandler(repository);
            var resourceId = Guid.NewGuid().ToString();
            var deletedResourceKey = new ResourceKey("Observation", resourceId);

            repository.DeleteAsync(Arg.Any<ResourceKey>(), true).Returns(deletedResourceKey);

            DeleteResourceResponse response = await handler.Handle(
                new DeleteResourceRequest("Observation", resourceId, true),
                CancellationToken.None);

            await repository.Received().DeleteAsync(Arg.Any<ResourceKey>(), true);

            Assert.Null(response.WeakETag);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenANonExistingResourceToDeleteRequest_WhenHandled_ThenTheResponseShouldHaveANullWeakETag(bool hardDelete)
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new DeleteResourceHandler(repository);
            var resourceId = Guid.NewGuid().ToString();
            var deletedResourceKey = new ResourceKey("Observation", resourceId);

            repository.DeleteAsync(Arg.Any<ResourceKey>(), hardDelete).Returns(deletedResourceKey);

            DeleteResourceResponse response = await handler.Handle(
                new DeleteResourceRequest("Observation", resourceId, hardDelete),
                CancellationToken.None);

            await repository.Received().DeleteAsync(Arg.Any<ResourceKey>(), hardDelete);

            Assert.Null(response.WeakETag);
        }
    }
}
