// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    /// <summary>
    /// Tests scenarios for the Conditional Upsert logic
    /// </summary>
    public partial class ResourceHandlerTests
    {
        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithNoIdAndNoExisting_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(SaveOutcomeType.Created, Samples.GetDefaultObservation());

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);
            var deserialized = result.Outcome.RawResourceElement.ToPoco<Observation>(Deserializers.ResourceDeserializer).ToResourceElement();
            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == deserialized.Id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithAnIdAndNoExisting_ThenTheServerShouldReturnTheCreatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(SaveOutcomeType.Created, Samples.GetDefaultObservation().UpdateId(id));

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithAnIdAndAWeakETag_WhenUpsertingConditionallyWithNoMatch_ThenTheClientWeakETagIsForwardedToPersistence()
        {
            // A conditional update that finds no match but carries an id becomes an update-as-create against a
            // row the conditional search never inspected. Dropping the client's If-Match here would let a stale
            // header silently overwrite that row, so the tag must reach persistence unchanged.
            string id = Guid.NewGuid().ToString();
            WeakETag weakETag = WeakETag.FromVersionId("7");

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(id),
                weakETag);

            await _mediator.SendAsync<UpsertResourceResponse>(message);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id && x.WeakETag == weakETag),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithAnIdAndAStaleWeakETag_WhenUpsertingConditionallyWithNoMatch_ThenPersistenceRejectsWithoutMutation()
        {
            // The row targeted by the id lives outside the conditional search criteria. Persistence is the only
            // component that can compare the supplied tag against that row, so the handler must hand the tag over
            // and let the store reject the write rather than mutating on a stale header.
            string id = Guid.NewGuid().ToString();

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(id),
                WeakETag.FromVersionId("stale"));

            var persisted = new List<ResourceWrapperOperation>();
            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(x =>
                {
                    ResourceWrapperOperation operation = x.ArgAt<ResourceWrapperOperation>(0);

                    // Stand in for a data store holding version "7" for this id.
                    if (operation.WeakETag != null && operation.WeakETag.VersionId != "7")
                    {
                        throw new PreconditionFailedException($"Version '{operation.WeakETag.VersionId}' is not current.");
                    }

                    persisted.Add(operation);
                    return new UpsertOutcome(operation.Wrapper, SaveOutcomeType.Updated);
                });

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<UpsertResourceResponse>(message));

            Assert.Empty(persisted);
        }

        [Fact]
        public async Task GivenAResourceWithNoIdAndAWeakETag_WhenUpsertingConditionallyWithNoMatch_ThenPreconditionFailedWithoutMutation()
        {
            // With no id and no match the server would create a brand new resource, and no created resource can
            // satisfy a supplied version. Silently ignoring the header would turn a guarded request into an
            // unguarded create, so the request must be rejected before any create is attempted.
            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Created,
                Samples.GetDefaultObservation().UpdateId(null),
                WeakETag.FromVersionId("7"));

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<UpsertResourceResponse>(message));

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapperOperation>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithNoIdAndNoWeakETag_WhenUpsertingConditionallyWithNoMatch_ThenTheResourceIsStillCreated()
        {
            // Regression guard for the approved behavior that conditional create after no match does not
            // require an If-Match header, even for versioned-update resource types.
            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Created,
                Samples.GetDefaultPatient().UpdateId(null));

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.WeakETag == null && x.ComparedVersion == null),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOneMatchingResourceWithUnavailableVersion_WhenUpsertingConditionally_ThenFailsClosedWithoutMutation()
        {
            // The conditional update forwards the version the search observed as the internal ComparedVersion
            // guard. If the data store projection did not surface one, continuing would silently downgrade a
            // guarded write into an unguarded one, so it must fail closed exactly like conditional delete.
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            mockResultEntry.Resource.Version.Returns((string)null);

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                null,
                mockResultEntry);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<UpsertResourceResponse>(message));

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapperOperation>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithNoId_WhenUpsertingConditionallyWithOneMatch_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns("7");

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                null,
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x =>
                    x.Wrapper.ResourceId == id &&
                    x.WeakETag == null &&
                    x.ComparedVersion == "7"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAVersionedUpdateResourceWithNoClientWeakETag_WhenUpsertingConditionallyWithOneMatch_ThenTheSearchVersionIsCarriedSeparately()
        {
            string id = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultPatient().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns("7");

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultPatient().UpdateId(null),
                null,
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);
            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x =>
                    x.Wrapper.ResourceId == id &&
                    x.WeakETag == null &&
                    x.RequireETagOnUpdate &&
                    x.ComparedVersion == "7"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithCorrectId_WhenUpsertingConditionallyWithOneMatch_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns("7");

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(id),
                null,
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id && x.WeakETag == null && x.ComparedVersion == "7"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithOneMatchAndMatchingWeakETag_ThenTheServerShouldForwardTheClientWeakETag()
        {
            string id = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns("7");

            WeakETag weakETag = WeakETag.FromVersionId("7");
            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                weakETag,
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.SendAsync<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id && x.WeakETag == weakETag),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithOneMatchAndStaleWeakETag_TheServerShouldFailWithoutMutation()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            mockResultEntry.Resource.Version.Returns("7");

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                WeakETag.FromVersionId("stale"),
                mockResultEntry);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.SendAsync<UpsertResourceResponse>(message));

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapperOperation>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithIncorrectId_WhenUpsertingConditionallyWithOneMatch_TheServerShouldFail()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()),
                null,
                mockResultEntry);

            await Assert.ThrowsAsync<BadRequestException>(async () => await _mediator.SendAsync<UpsertResourceResponse>(message));
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                null,
                mockResultEntry1,
                mockResultEntry2);

            await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _mediator.SendAsync<UpsertResourceResponse>(message));
        }

        private ConditionalUpsertResourceRequest SetupConditionalUpdate(
            SaveOutcomeType outcomeType,
            ResourceElement requestResource,
            params SearchResultEntry[] searchResults)
            => SetupConditionalUpdate(outcomeType, requestResource, null, searchResults);

        private ConditionalUpsertResourceRequest SetupConditionalUpdate(
            SaveOutcomeType outcomeType,
            ResourceElement requestResource,
            WeakETag weakETag,
            params SearchResultEntry[] searchResults)
        {
            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_tag", Guid.NewGuid().ToString()) };

            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(new SearchResult(searchResults, null, null, Enumerable.Empty<Tuple<string, string>>().ToArray()));

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapperOperation>(0).Wrapper, outcomeType));

            var message = new ConditionalUpsertResourceRequest(requestResource, list, weakETag: weakETag);

            return message;
        }
    }
}
