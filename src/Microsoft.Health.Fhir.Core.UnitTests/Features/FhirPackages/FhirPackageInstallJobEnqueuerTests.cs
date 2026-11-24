// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string PackageId = "example.package";
        private const string Version = "1.0.0";
        private const string Source = "trusted";

        [Fact]
        public async Task GivenAnActiveJobForTheSamePackage_WhenEnqueuing_ThenTheActiveJobIsReturned()
        {
            // Arrange
            var queue = Substitute.For<IQueueClient>();
            var other = ActiveJob(1, "other.package");
            var active = ActiveJob(2, PackageId);
            queue.GetActiveJobsByQueueTypeAsync((byte)QueueType.FhirPackageInstall, false, Arg.Any<CancellationToken>())
                .Returns(new[] { other, active });
            var enqueuer = new FhirPackageInstallJobEnqueuer(queue);

            // Act
            JobInfo result = await enqueuer.EnqueueAsync(PackageId, Version, Source, CancellationToken.None);

            // Assert
            Assert.Same(active, result);
            await queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, default, default, default);
        }

        [Fact]
        public async Task GivenNoActiveJobForThePackage_WhenEnqueuingTwice_ThenEachDefinitionHasAUniqueId()
        {
            // Arrange
            var queue = Substitute.For<IQueueClient>();
            queue.GetActiveJobsByQueueTypeAsync((byte)QueueType.FhirPackageInstall, false, Arg.Any<CancellationToken>())
                .Returns(new[] { ActiveJob(1, "other.package") });
            var definitions = new List<string>();
            queue.EnqueueAsync((byte)QueueType.FhirPackageInstall, Arg.Any<string[]>(), null, false, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    definitions.Add(call.Arg<string[]>()[0]);
                    return Task.FromResult<IReadOnlyList<JobInfo>>(new[] { new JobInfo { Id = definitions.Count + 10 } });
                });
            var enqueuer = new FhirPackageInstallJobEnqueuer(queue);

            // Act
            JobInfo first = await enqueuer.EnqueueAsync(PackageId, Version, Source, CancellationToken.None);
            JobInfo second = await enqueuer.EnqueueAsync(PackageId, Version, Source, CancellationToken.None);

            // Assert
            Assert.NotEqual(first.Id, second.Id);
            FhirPackageInstallJobDefinition[] actual = definitions
                .Select(JsonConvert.DeserializeObject<FhirPackageInstallJobDefinition>)
                .ToArray();
            Assert.All(actual, definition =>
            {
                Assert.Equal(PackageId, definition.PackageId);
                Assert.Equal(Version, definition.Version);
                Assert.Equal(Source, definition.Source);
                Assert.True(Guid.TryParse(definition.Id, out _));
            });
            Assert.NotEqual(actual[0].Id, actual[1].Id);
        }

        [Fact]
        public async Task GivenTheQueueReturnsNoJob_WhenEnqueuing_ThenAnExceptionIsThrown()
        {
            // Arrange
            var queue = Substitute.For<IQueueClient>();
            queue.GetActiveJobsByQueueTypeAsync((byte)QueueType.FhirPackageInstall, false, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<JobInfo>());
            queue.EnqueueAsync((byte)QueueType.FhirPackageInstall, Arg.Any<string[]>(), null, false, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<JobInfo>());
            var enqueuer = new FhirPackageInstallJobEnqueuer(queue);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => enqueuer.EnqueueAsync(PackageId, Version, Source, CancellationToken.None));
        }

        private static JobInfo ActiveJob(long id, string packageId) => new JobInfo
        {
            Id = id,
            Status = JobStatus.Running,
            Definition = JsonConvert.SerializeObject(new FhirPackageInstallJobDefinition
            {
                PackageId = packageId,
                Version = Version,
                Source = Source,
                Id = Guid.NewGuid().ToString(),
            }),
        };
    }
}
