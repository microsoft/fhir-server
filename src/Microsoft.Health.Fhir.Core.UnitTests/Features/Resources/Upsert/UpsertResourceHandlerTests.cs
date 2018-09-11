// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Upsert
{
    public class UpsertResourceHandlerTests
    {
        [Fact]
        public async Task GivenAnUpsertResourceRequest_WhenHandled_ThenTheResourceShouldBeUpsertedIntoTheRepository()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new UpsertResourceHandler(repository);
            repository.UpsertAsync(Arg.Any<Resource>(), Arg.Any<WeakETag>()).Returns(new SaveOutcome(Samples.GetDefaultObservation(), SaveOutcomeType.Created));
            var observation = Samples.GetDefaultObservation();

            repository.UpsertAsync(Arg.Any<Resource>()).Returns(new SaveOutcome(observation, SaveOutcomeType.Created));

            await handler.Handle(
                new UpsertResourceRequest(observation),
                CancellationToken.None);

            await repository.Received().UpsertAsync(Arg.Any<Observation>());
        }

        [Fact]
        public async Task GivenAnUpsertResourceRequestWithMatchingId_WhenHandled_ThenTheResourceShouldBeUpsertedIntoTheRepository()
        {
            var repository = Substitute.For<IFhirRepository>();
            var handler = new UpsertResourceHandler(repository);
            repository.UpsertAsync(Arg.Any<Resource>(), Arg.Any<WeakETag>()).Returns(new SaveOutcome(Samples.GetDefaultObservation(), SaveOutcomeType.Created));

            var weakETag = WeakETag.FromVersionId(Guid.NewGuid().ToString());
            var observation = Samples.GetDefaultObservation();
            observation.VersionId = weakETag.VersionId;
            observation.Id = Guid.NewGuid().ToString();

            await handler.Handle(
                new UpsertResourceRequest(observation, weakETag),
                CancellationToken.None);

            await repository.Received().UpsertAsync(observation, weakETag);
        }
    }
}
