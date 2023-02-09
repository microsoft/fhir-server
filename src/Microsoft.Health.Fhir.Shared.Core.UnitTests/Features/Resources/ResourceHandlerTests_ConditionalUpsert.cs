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

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);
            var deserialized = result.Outcome.RawResourceElement.ToPoco<Observation>(Deserializers.ResourceDeserializer).ToResourceElement();
            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == deserialized.Id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithAnIdAndNoExisting_ThenTheServerShouldReturnTheCreatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(SaveOutcomeType.Created, Samples.GetDefaultObservation().UpdateId(id));

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithNoId_WhenUpsertingConditionallyWithOneMatch_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();
            string version = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns(version);

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id && x.WeakETag != null && x.WeakETag.VersionId == version),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithCorrectId_WhenUpsertingConditionallyWithOneMatch_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();
            string version = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns(version);

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(id),
                mockResultEntry);

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => x.Wrapper.ResourceId == id && x.WeakETag != null && x.WeakETag.VersionId == version),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResourceWithIncorrectId_WhenUpsertingConditionallyWithOneMatch_TheServerShouldFail()
        {
            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()),
                mockResultEntry);

            await Assert.ThrowsAsync<BadRequestException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        [Fact]
        public async Task GivenAResource_WhenUpsertingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalUpsertResourceRequest message = SetupConditionalUpdate(
                SaveOutcomeType.Updated,
                Samples.GetDefaultObservation(),
                mockResultEntry1,
                mockResultEntry2);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        private ConditionalUpsertResourceRequest SetupConditionalUpdate(
            SaveOutcomeType outcomeType,
            ResourceElement requestResource,
            params SearchResultEntry[] searchResults)
        {
            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_tag", Guid.NewGuid().ToString()) };

            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(new SearchResult(searchResults, null, null, Enumerable.Empty<Tuple<string, string>>().ToArray()));

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapperOperation>(0).Wrapper, outcomeType));

            var message = new ConditionalUpsertResourceRequest(requestResource, list);

            return message;
        }
    }
}
