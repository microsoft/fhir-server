// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BackgroundJobs)]
    public class FhirPackageInstallJobEnqueuerTests
    {
        [Fact]
        public async Task GivenConcurrentEnqueues_WhenTheFirstReturnsNoJob_ThenTheWinnerIsReturned()
        {
            // Arrange
            var queue = Substitute.For<IQueueClient>();
            var winner = new JobInfo { Id = 10 };
            queue.EnqueueAsync((byte)QueueType.FhirPackageInstall, Arg.Any<string[]>(), null, false, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<JobInfo>(), new[] { winner });
            var enqueuer = new FhirPackageInstallJobEnqueuer(queue);

            // Act
            JobInfo result = await enqueuer.EnqueueAsync(Definition(), CancellationToken.None);

            // Assert
            Assert.Same(winner, result);
            await queue.Received(2).EnqueueAsync((byte)QueueType.FhirPackageInstall, Arg.Any<string[]>(), null, false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAFailedJob_WhenEnqueuingARetry_ThenItsDefinitionHasADistinctRetryGeneration()
        {
            // Arrange
            var queue = Substitute.For<IQueueClient>();
            var definitions = new List<string>();
            queue.EnqueueAsync((byte)QueueType.FhirPackageInstall, Arg.Any<string[]>(), null, false, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    definitions.Add(call.Arg<string[]>()[0]);
                    return Task.FromResult<IReadOnlyList<JobInfo>>(new[] { new JobInfo { Id = definitions.Count } });
                });
            var enqueuer = new FhirPackageInstallJobEnqueuer(queue);
            FhirPackageInstallJobDefinition first = Definition();
            FhirPackageInstallJobDefinition retry = Definition();
            retry.RetryOfJobId = 42;

            // Act
            await enqueuer.EnqueueAsync(first, CancellationToken.None);
            await enqueuer.EnqueueAsync(retry, CancellationToken.None);

            // Assert
            Assert.NotEqual(definitions[0], definitions[1]);
            FhirPackageInstallJobDefinition actual = JsonConvert.DeserializeObject<FhirPackageInstallJobDefinition>(definitions[1]);
            Assert.Equal(42, actual.RetryOfJobId);
            Assert.Equal(first.PackageId, actual.PackageId);
            Assert.Equal(first.Version, actual.Version);
        }

        private static FhirPackageInstallJobDefinition Definition() => new FhirPackageInstallJobDefinition
        {
            PackageId = "example.package",
            Version = "1.0.0",
            Source = "trusted",
        };
    }
}
