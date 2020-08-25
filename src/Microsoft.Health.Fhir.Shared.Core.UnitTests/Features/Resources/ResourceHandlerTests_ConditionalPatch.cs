// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Patch;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    /// <summary>
    /// Tests scenarios for the Conditional Patch logic
    /// </summary>
    public partial class ResourceHandlerTests
    {
        [Fact]
        public async Task GivenAResource_WhenPatchingConditionallyWithNoExisting_ThenTheServerShouldThrowNotFoundException()
        {
            ConditionalPatchResourceRequest message = SetupConditionalPatch(Samples.GetDefaultObservation().InstanceType);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _mediator.Send<PatchResourceResponse>(message));
        }

        [Fact]
        public async Task GivenAResource_WhenPatchingConditionallyWithOneMatch_ThenTheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            string id = Guid.NewGuid().ToString();
            string version = Guid.NewGuid().ToString();

            var mockResultEntry = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(id), false));
            mockResultEntry.Resource.Version.Returns(version);

            ConditionalPatchResourceRequest message = SetupConditionalPatch(
                Samples.GetDefaultObservation().InstanceType,
                mockResultEntry);

            PatchResourceResponse result = await _mediator.Send<PatchResourceResponse>(message);

            Assert.Equal(SaveOutcomeType.Updated, result.Outcome.Outcome);

            await _fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapper>(x => x.ResourceId == id),
                Arg.Is<WeakETag>(x => x.VersionId == version),
                false,
                true,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAResource_WhenPatchingConditionallyWithMultipleMatches_TheServerThrowPreconditionFailedException()
        {
            var mockResultEntry1 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));
            var mockResultEntry2 = new SearchResultEntry(CreateMockResourceWrapper(Samples.GetDefaultObservation().UpdateId(Guid.NewGuid().ToString()), false));

            ConditionalPatchResourceRequest message = SetupConditionalPatch(
                Samples.GetDefaultObservation().InstanceType,
                mockResultEntry1,
                mockResultEntry2);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _mediator.Send<PatchResourceResponse>(message));
        }

        private ConditionalPatchResourceRequest SetupConditionalPatch(
            string resourceType,
            params SearchResultEntry[] searchResults)
        {
            IReadOnlyList<Tuple<string, string>> list = new[] { Tuple.Create("_tag", Guid.NewGuid().ToString()) };

            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None)
                .Returns(new SearchResult(searchResults, Enumerable.Empty<Tuple<string, string>>().ToArray(), Enumerable.Empty<(string, string)>().ToArray(), null));

            _fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapper>(), Arg.Any<WeakETag>(), false, true, Arg.Any<CancellationToken>())
                .Returns(x => new UpsertOutcome(x.ArgAt<ResourceWrapper>(0), SaveOutcomeType.Updated));

            var message = new ConditionalPatchResourceRequest(resourceType, Substitute.For<IPatchDocument>(), list);

            return message;
        }
    }
}
