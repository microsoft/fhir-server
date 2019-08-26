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
        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithNoExisting_ThenTheServerShouldReturnTheResourceSuccessfully()
        {
            ConditionalCreateResourceRequest message = SetupConditionalCreate(Samples.GetDefaultObservation());

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Created, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(Arg.Is<ResourceWrapper>(x => x.ResourceId == result.Outcome.Resource.Id), null, true, true, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithOneMatch_ThenTheServerShouldReturnTheResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();
            string version = Guid.NewGuid().ToString();

            ResourceWrapper mockResourceWrapper = CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false);
            mockResourceWrapper.Version.Returns(version);

            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                mockResourceWrapper);

            UpsertResourceResponse result = await _mediator.Send<UpsertResourceResponse>(message);

            Assert.Null(result);

            await _fhirDataStore.DidNotReceive().UpsertAsync(
                Arg.Any<ResourceWrapper>(),
                Arg.Any<WeakETag>(),
                true,
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenCreatingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            ResourceWrapper mockResourceWrapper1 = CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false);
            ResourceWrapper mockResourceWrapper2 = CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false);

            ConditionalCreateResourceRequest message = SetupConditionalCreate(
                Samples.GetDefaultObservation(),
                mockResourceWrapper1,
                mockResourceWrapper2);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<UpsertResourceResponse>(message));
        }

        private ConditionalCreateResourceRequest SetupConditionalCreate(ResourceElement requestResource, params ResourceWrapper[] searchResults)
        {
            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_tag", Guid.NewGuid().ToString()) };

            _searchService.SearchAsync(Arg.Any<string>(), list, CancellationToken.None)
                .Returns(new SearchResult(searchResults, Enumerable.Empty<Tuple<string, string>>().ToArray(), null));

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), true, true, Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapper>(0), SaveOutcomeType.Created));

            var message = new ConditionalCreateResourceRequest(requestResource, list);

            return message;
        }
    }
}
