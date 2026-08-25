// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    /// <summary>
    /// Tests scenarios for the Conditional Delete logic
    /// </summary>
    public partial class ResourceHandlerTests
    {
        [Fact]
        public async Task GivenNoExistingResources_WhenDeletingConditionally_TheServerShouldReturn()
        {
            ConditionalDeleteResourceRequest message = SetupConditionalDelete(KnownResourceTypes.Observation, DefaultSearchParams);

            DeleteResourceResponse result = await _mediator.SendAsync(message);

            Assert.Equal(0, result.ResourcesDeleted);
        }

        [Fact]
        public async Task GivenOneMatchingResource_WhenDeletingConditionally_TheServerShouldDeleteSuccessfully()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: false,
                count: 1,
                mockResultEntry);

            DeleteResourceResponse result = await _mediator.SendAsync(message);

            Assert.NotNull(result);
            Assert.Equal(1, result.ResourcesDeleted);

            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.IsDeleted), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOneMatchingResource_WhenDeletingConditionallyWithHardDeleteFlag_TheServerShouldDeleteSuccessfully()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: true,
                count: 1,
                mockResultEntry);

            DeleteResourceResponse result = await _mediator.SendAsync(message);

            Assert.NotNull(result);
            Assert.Equal(1, result.ResourcesDeleted);

            await _fhirDataStore.DidNotReceive().UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>());

            await _fhirDataStore.Received().HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOneMatchingResource_WhenDeletingConditionallyWithStaleWeakETag_TheServerShouldFailWithoutMutation()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            mockResultEntry.Resource.Version.Returns("7");

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: true,
                count: 1,
                weakETag: WeakETag.FromVersionId("stale"),
                mockResultEntry);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<DeleteResourceResponse>(message));

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapperOperation>(),
                Arg.Any<CancellationToken>());
            await _fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Any<ResourceKey>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOneMatchingResourceWithIncludeResults_WhenDeletingConditionallyWithStaleWeakETag_TheServerShouldFailWithoutMutation()
        {
            var matchResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            matchResultEntry.Resource.Version.Returns("7");

            var includeResultEntry = new SearchResultEntry(
                CreateMockResourceWrapper(Samples.GetDefaultPatient().UpdateId(Guid.NewGuid().ToString()), false),
                ValueSets.SearchEntryMode.Include);

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: true,
                count: 1,
                weakETag: WeakETag.FromVersionId("stale"),
                matchResultEntry,
                includeResultEntry);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<DeleteResourceResponse>(message));

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapperOperation>(),
                Arg.Any<CancellationToken>());
            await _fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Any<ResourceKey>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOneMatchingResourceWithIncludeResults_WhenSoftDeletingConditionallyWithMatchingWeakETag_ThenOnlyTheMatchOperationCarriesTheETagAndPolicy()
        {
            string matchId = Guid.NewGuid().ToString();
            string includeId = Guid.NewGuid().ToString();
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var matchResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultPatient().UpdateId(matchId), false));
            matchResultEntry.Resource.Version.Returns(weakETag.VersionId);
            var includeResultEntry = new SearchResultEntry(
                CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(includeId), false),
                ValueSets.SearchEntryMode.Include);
            var mergedOperations = new List<ResourceWrapperOperation>();
            _fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    mergedOperations.AddRange(callInfo.ArgAt<IReadOnlyList<ResourceWrapperOperation>>(0));
                    return Task.FromResult(MergeOutcome.Empty);
                });

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Patient,
                DefaultSearchParams,
                hardDelete: false,
                count: 1,
                weakETag,
                matchResultEntry,
                includeResultEntry);

            await _mediator.SendAsync<DeleteResourceResponse>(message);

            ResourceWrapperOperation matchOperation = mergedOperations.Single(operation => operation.Wrapper.ResourceId == matchId);
            ResourceWrapperOperation includeOperation = mergedOperations.Single(operation => operation.Wrapper.ResourceId == includeId);
            Assert.Equal(weakETag, matchOperation.WeakETag);
            Assert.True(matchOperation.RequireETagOnUpdate);
            Assert.Null(includeOperation.WeakETag);
            Assert.False(includeOperation.RequireETagOnUpdate);
        }

        [Fact]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionally_TheServerShouldReturnError()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: false,
                count: 1,
                mockResultEntry1,
                mockResultEntry2);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<DeleteResourceResponse>(message));
        }

        [Fact]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalDeleteResourceRequest message = SetupConditionalDelete(
                KnownResourceTypes.Observation,
                DefaultSearchParams,
                hardDelete: false,
                count: 100,
                mockResultEntry1,
                mockResultEntry2);

            DeleteResourceResponse result = await _mediator.SendAsync(message);

            Assert.NotNull(result);
            Assert.Equal(2, result.ResourcesDeleted);

            await _fhirDataStore.Received(1).MergeAsync(Arg.Is<IReadOnlyList<ResourceWrapperOperation>>(list => list.All(item => item.Wrapper.IsDeleted)), Arg.Any<CancellationToken>());
        }

        private ConditionalDeleteResourceRequest SetupConditionalDelete(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> list = null,
            bool hardDelete = false,
            int count = 1,
            params SearchResultEntry[] searchResults)
            => SetupConditionalDelete(resourceType, list, hardDelete, count, null, searchResults);

        private ConditionalDeleteResourceRequest SetupConditionalDelete(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> list,
            bool hardDelete,
            int count,
            WeakETag weakETag,
            params SearchResultEntry[] searchResults)
        {
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), resourceVersionTypes: Arg.Any<ResourceVersionType>(), onlyIds: Arg.Any<bool>())
                .Returns(new SearchResult(searchResults, null, null, Enumerable.Empty<Tuple<string, string>>().ToArray()));

            if (hardDelete)
            {
                _fhirDataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            }
            else
            {
                _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                    .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapperOperation>(0).Wrapper, SaveOutcomeType.Updated));
            }

            var message = new ConditionalDeleteResourceRequest(
                resourceType,
                list,
                hardDelete ? DeleteOperation.HardDelete : DeleteOperation.SoftDelete,
                count,
                bundleResourceContext: null,
                weakETag: weakETag);

            return message;
        }
    }
}
