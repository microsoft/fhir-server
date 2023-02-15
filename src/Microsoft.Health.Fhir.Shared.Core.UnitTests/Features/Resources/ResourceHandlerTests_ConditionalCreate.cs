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
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    /// <summary>
    /// Tests scenarios for the Conditional Create logic
    /// </summary>
    public partial class ResourceHandlerTests
    {
        private static Tuple<string, string>[] DefaultSearchParams => new[] { Tuple.Create("_tag", Guid.NewGuid().ToString()) };

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithNoExisting_ThenTheServerShouldReturnTheResourceSuccessfully()
        {
            ConditionalCreateResourceRequest message = SetupConditionalCreate(Samples.GetDefaultObservation(), DefaultSearchParams);

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == result.Outcome.RawResourceElement.Id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithOneMatch_ThenTheServerShouldReturnTheResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();
            string version = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns(version);

            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                DefaultSearchParams,
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Null(result);

            await _fhirDataStore.DidNotReceive().UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                DefaultSearchParams,
                mockResultEntry1,
                mockResultEntry2);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithFilteredSearchParams_TheServerShouldFail()
        {
            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                new List<Tuple<string, string>>
                {
                    Tuple.Create("_count", "1"),
                    Tuple.Create("_summary", "count"),
                });

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithUnsupportedParams_TheServerShouldFail()
        {
            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                new List<Tuple<string, string>>
                {
                    Tuple.Create("unknown1", "unknown"),
                });

            var searchResult = new SearchResult(
                Enumerable.Empty<SearchResultEntry>(),
                null,
                null,
                new[] { Tuple.Create("unknown1", "unknown") });

            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(searchResult);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        private ConditionalCreateResourceRequest SetupConditionalCreate(
            ResourceElement requestResource,
            IReadOnlyList<Tuple<string, string>> list = null,
            params SearchResultEntry[] searchResults)
        {
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(new SearchResult(searchResults, null, null, Enumerable.Empty<Tuple<string, string>>().ToArray()));

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapperOperation>(0).Wrapper, SaveOutcomeType.Created));

            var message = new ConditionalCreateResourceRequest(requestResource, list);

            return message;
        }
    }
}
