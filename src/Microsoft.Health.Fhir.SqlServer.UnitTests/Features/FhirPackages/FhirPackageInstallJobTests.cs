// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BackgroundJobs)]
    public class FhirPackageInstallJobTests
    {
        private const string UrlA = "https://example.org/SearchParameter/a";
        private const string UrlB = "https://example.org/SearchParameter/b";
        private static readonly DateTimeOffset LastUpdated = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        private readonly IFhirPackageAcquirer _acquirer = Substitute.For<IFhirPackageAcquirer>();
        private readonly IFhirPackageInstaller _installer = Substitute.For<IFhirPackageInstaller>();
        private readonly IFhirPackageInstallVerifier _verifier = Substitute.For<IFhirPackageInstallVerifier>();
        private readonly ISearchParameterStatusManager _statusManager = Substitute.For<ISearchParameterStatusManager>();
        private readonly ISearchParameterDefinitionManager _definitions = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly FhirPackageEntry _entryA = new FhirPackageEntry("package/a.json", KnownResourceTypes.SearchParameter, "{}");
        private readonly FhirPackageEntry _entryB = new FhirPackageEntry("package/b.json", KnownResourceTypes.SearchParameter, "{}");
        private readonly FhirPackage _package;
        private readonly FhirPackageInstallJob _job;

        public FhirPackageInstallJobTests()
        {
            _package = new FhirPackage(
                new FhirPackageDescriptor("example.package", "1.0.0", "sha512-test"),
                new[] { _entryA, _entryB, new FhirPackageEntry("package/profile.json", "StructureDefinition", "{}") });
            _definitions.AllSearchParameters.Returns(Array.Empty<SearchParameterInfo>());
            _definitions.SearchParameterHashMap.Returns(new Dictionary<string, string>());
            _acquirer.AcquireAsync("example.package", "1.0.0", "trusted", Arg.Any<CancellationToken>()).Returns(_package);

            var services = new ServiceCollection()
                .AddSingleton(_acquirer)
                .AddSingleton(_installer)
                .AddSingleton(_verifier)
                .AddSingleton(_definitions)
                .BuildServiceProvider();
            var operations = new OperationsConfiguration
            {
                FhirPackageInstall = new FhirPackageInstallJobConfiguration
                {
                    CacheUpdateMaxWaitMultiplier = 1,
                    ActiveHostsEventsMultiplier = 1,
                },
            };
            _job = new FhirPackageInstallJob(
                services,
                _statusManager,
                Options.Create(operations),
                Options.Create(new CoreFeatureConfiguration { SearchParameterCacheRefreshIntervalSeconds = 1 }),
                NullLogger<FhirPackageInstallJob>.Instance);
        }

        [Fact]
        public async Task GivenACommittedPackage_WhenTheJobRecovers_ThenItVerifiesWithoutWritingAgain()
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>()).Returns(Installed());
            SetCacheProof(LastUpdated, SearchParameterCacheFingerprint.Calculate(_definitions));

            // Act
            FhirPackageInstallJobResult result = await ExecuteAsync();

            // Assert
            Assert.Equal(FhirPackageInstallJobState.Converged, result.State);
            Assert.Equal(new[] { UrlA, UrlB }, result.SearchParameterUrls);
            Assert.Equal("sha512-test", result.Integrity);
            Assert.Contains(result.Issues, issue => issue.Diagnostics.Contains("StructureDefinition", StringComparison.Ordinal));
            await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default, default);
            await _verifier.Received(2).InspectAsync(_package, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAPartiallyInstalledPackage_WhenTheJobRuns_ThenOnlyMissingEntriesAreWritten()
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>())
                .Returns(Inspection(_entryB), Installed());
            SetCacheProof(LastUpdated, SearchParameterCacheFingerprint.Calculate(_definitions));

            // Act
            FhirPackageInstallJobResult result = await ExecuteAsync();

            // Assert
            Assert.Equal(FhirPackageInstallJobState.Converged, result.State);
            await _installer.Received(1).InstallAsync(
                Arg.Is<FhirPackage>(p => p.ResourceEntries.Contains(_entryB) && !p.ResourceEntries.Contains(_entryA)),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAConcurrentInstall_WhenTheWriteConflicts_ThenTheJobRechecksAndWritesOnlyWhatRemains()
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>())
                .Returns(Inspection(_entryA, _entryB), Inspection(_entryB), Installed());
            int calls = 0;
            _installer.InstallAsync(Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns(_ => ++calls == 1
                    ? throw new FhirPackageWriteConflictException("concurrent write", isRetryable: false)
                    : Task.FromResult<FhirPackageInstallResult>(null));
            SetCacheProof(LastUpdated, SearchParameterCacheFingerprint.Calculate(_definitions));

            // Act
            FhirPackageInstallJobResult result = await ExecuteAsync();

            // Assert
            Assert.Equal(FhirPackageInstallJobState.Converged, result.State);
            await _installer.Received(2).InstallAsync(Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>());
            await _installer.Received(1).InstallAsync(
                Arg.Is<FhirPackage>(p => p.ResourceEntries.Contains(_entryB) && !p.ResourceEntries.Contains(_entryA)),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnotherWorkerFinishedBeforeDataArrived_WhenTheStoreIsNoLongerEmpty_ThenTheJobRecovers()
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>()).Returns(Inspection(_entryA), Installed());
            _installer.InstallAsync(Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns<Task<FhirPackageInstallResult>>(_ => throw new FhirPackageStoreNotEmptyException("example.package", "1.0.0"));
            SetCacheProof(LastUpdated, SearchParameterCacheFingerprint.Calculate(_definitions));

            // Act
            FhirPackageInstallJobResult result = await ExecuteAsync();

            // Assert
            Assert.Equal(FhirPackageInstallJobState.Converged, result.State);
        }

        [Fact]
        public async Task GivenAConflictWithNoConcurrentProgress_WhenTheJobRuns_ThenTheOriginalFailureIsReported()
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>()).Returns(Inspection(_entryA));
            _installer.InstallAsync(Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns<Task<FhirPackageInstallResult>>(_ => throw new FhirPackageWriteConflictException("different definition", isRetryable: false));

            // Act
            JobExecutionException exception = await Assert.ThrowsAsync<JobExecutionException>(() => _job.ExecuteAsync(Job(), CancellationToken.None));

            // Assert
            var result = Assert.IsType<FhirPackageInstallJobResult>(exception.Error);
            Assert.False(result.IsRetryable);
            Assert.Equal(FhirPackageInstallJobState.Verifying, result.State);
            Assert.Contains("different definition", result.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAnOldWatermarkOrDifferentCache_WhenWaitingForConvergence_ThenTheCommittedJobTimesOut(bool oldWatermark)
        {
            // Arrange
            _verifier.InspectAsync(_package, Arg.Any<CancellationToken>()).Returns(Installed());
            SetCacheProof(
                oldWatermark ? LastUpdated.AddTicks(-1) : LastUpdated,
                oldWatermark ? SearchParameterCacheFingerprint.Calculate(_definitions) : "a-different-cache");

            // Act
            JobExecutionException exception = await Assert.ThrowsAsync<JobExecutionException>(() => _job.ExecuteAsync(Job(), CancellationToken.None));

            // Assert
            var result = Assert.IsType<FhirPackageInstallJobResult>(exception.Error);
            Assert.Equal(FhirPackageInstallJobState.Committed, result.State);
            Assert.True(result.IsRetryable);
            Assert.Contains("timed out", result.Message, StringComparison.Ordinal);
            await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default, default);
        }

        [Fact]
        public async Task GivenATransientAcquisitionFailure_WhenTheJobRuns_ThenItReportsARetryableFailure()
        {
            // Arrange
            _acquirer.AcquireAsync("example.package", "1.0.0", "trusted", Arg.Any<CancellationToken>())
                .Returns<Task<FhirPackage>>(_ => throw new FhirPackageAcquisitionException("registry unavailable", isRetryable: true));

            // Act
            JobExecutionException exception = await Assert.ThrowsAsync<JobExecutionException>(() => _job.ExecuteAsync(Job(), CancellationToken.None));

            // Assert
            var result = Assert.IsType<FhirPackageInstallJobResult>(exception.Error);
            Assert.True(result.IsRetryable);
            Assert.Equal(FhirPackageInstallJobState.Verifying, result.State);
            await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default, default);
        }

        [Theory]
        [InlineData(1205, true, false)]
        [InlineData(18456, false, false)]
        [InlineData(1205, true, true)]
        [InlineData(18456, false, true)]
        public async Task GivenASqlFailure_WhenTheJobRuns_ThenItReportsRetryabilityAndTheLastVerifiedState(
            int errorNumber,
            bool retryable,
            bool afterCommit)
        {
            // Arrange
            var failure = SqlExceptionFactory.GetSqlException(errorNumber, "SQL failure");
            if (afterCommit)
            {
                _verifier.InspectAsync(_package, Arg.Any<CancellationToken>()).Returns(Installed());
                _statusManager.CheckCacheConsistencyAsync(
                        Arg.Any<DateTime>(),
                        Arg.Any<DateTime>(),
                        Arg.Any<CancellationToken>(),
                        SearchParameterCacheFingerprint.UpdateProcessName)
                    .Returns<Task<CacheConsistencyResult>>(_ => throw failure);
            }
            else
            {
                _verifier.InspectAsync(_package, Arg.Any<CancellationToken>())
                    .Returns<Task<FhirPackageInstallInspection>>(_ => throw failure);
            }

            // Act
            JobExecutionException exception = await Assert.ThrowsAsync<JobExecutionException>(() => _job.ExecuteAsync(Job(), CancellationToken.None));

            // Assert
            var result = Assert.IsType<FhirPackageInstallJobResult>(exception.Error);
            Assert.Equal(retryable, result.IsRetryable);
            Assert.Equal(afterCommit ? FhirPackageInstallJobState.Committed : FhirPackageInstallJobState.Verifying, result.State);
            Assert.False(exception.IsCustomerCaused);
            Assert.Same(failure, exception.InnerException);
        }

        [Fact]
        public async Task GivenCancellationDuringAcquisition_WhenTheJobRuns_ThenCancellationPropagatesWithoutWriting()
        {
            // Arrange
            using var source = new CancellationTokenSource();
            await source.CancelAsync();
            _acquirer.AcquireAsync("example.package", "1.0.0", "trusted", source.Token)
                .Returns(Task.FromCanceled<FhirPackage>(source.Token));

            // Act and assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _job.ExecuteAsync(Job(), source.Token));
            await _installer.DidNotReceiveWithAnyArgs().InstallAsync(default, default);
        }

        private static JobInfo Job() => new JobInfo
        {
            Id = 42,
            Definition = JsonConvert.SerializeObject(new FhirPackageInstallJobDefinition
            {
                PackageId = "example.package",
                Version = "1.0.0",
                Source = "trusted",
            }),
        };

        private static FhirPackageInstallInspection Inspection(params FhirPackageEntry[] missing) =>
            new FhirPackageInstallInspection(new[] { UrlA, UrlB }, missing, LastUpdated);

        private static FhirPackageInstallInspection Installed() => Inspection();

        private async Task<FhirPackageInstallJobResult> ExecuteAsync() =>
            JsonConvert.DeserializeObject<FhirPackageInstallJobResult>(await _job.ExecuteAsync(Job(), CancellationToken.None));

        private void SetCacheProof(DateTimeOffset version, string fingerprint)
        {
            string value = version.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "|" + fingerprint;
            _statusManager.CheckCacheConsistencyAsync(
                    Arg.Any<DateTime>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>(),
                    SearchParameterCacheFingerprint.UpdateProcessName)
                .Returns(new CacheConsistencyResult { IsConsistent = true, ActiveHosts = 2, ConvergedHosts = 2, SearchParameterVersion = value });
        }
    }
}
