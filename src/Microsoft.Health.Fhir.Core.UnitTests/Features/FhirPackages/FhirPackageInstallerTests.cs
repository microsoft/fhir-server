// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class FhirPackageInstallerTests
    {
        private const string SearchParameterType = "SearchParameter";

        private static readonly FhirPackageDescriptor Descriptor = new FhirPackageDescriptor("example.package", "1.0.0", "sha512-abc");

        [Fact]
        public async Task GivenSupportedAndUnsupportedResources_WhenInstalling_ThenOnlySupportedResourcesAreInstalledAndIgnoredTypesAreNamed()
        {
            // Arrange
            IFhirPackageInstallPlan plan = CreatePlan(SearchParameterType, "http://example.org/sp/a", "http://example.org/sp/b");
            IFhirPackageResourceHandler handler = CreateHandler(SearchParameterType, plan);
            var package = new FhirPackage(
                Descriptor,
                new[]
                {
                    Entry("package/SearchParameter-a.json", SearchParameterType),
                    Entry("package/SearchParameter-b.json", SearchParameterType),
                    Entry("package/StructureDefinition-x.json", "StructureDefinition"),
                    Entry("package/ValueSet-x.json", "ValueSet"),
                    Entry("package/ValueSet-y.json", "ValueSet"),
                },
                ignoredEntries: new[]
                {
                    Entry("package/example/Patient-1.json", "Patient"),
                    Entry("package/.index.json", null),
                },
                ignoredDependencies: new[] { "example.other.package" });

            // Act
            FhirPackageInstallResult result = await CreateInstaller(handler).InstallAsync(package, CancellationToken.None);

            // Assert
            await handler.Received(1).ValidateAsync(
                Arg.Is<IReadOnlyList<FhirPackageEntry>>(x => x.Count == 2 && x.All(e => e.ResourceType == SearchParameterType)),
                package,
                Arg.Any<CancellationToken>());
            await plan.Received(1).ApplyAsync(Arg.Any<CancellationToken>());

            Assert.Same(Descriptor, result.Descriptor);
            Assert.Equal(new[] { "http://example.org/sp/a", "http://example.org/sp/b" }, result.InstalledResources[SearchParameterType]);
            Assert.Equal(3, result.IgnoredResourceTypes.Count);
            Assert.Equal(1, result.IgnoredResourceTypes["Patient"]);
            Assert.Equal(1, result.IgnoredResourceTypes["StructureDefinition"]);
            Assert.Equal(2, result.IgnoredResourceTypes["ValueSet"]);
            Assert.Equal(new[] { "example.other.package" }, result.IgnoredDependencies);

            Assert.All(result.Issues, i => Assert.Equal("Information", i.Severity));
            Assert.Contains(result.Issues, i => i.Diagnostics.Contains("'StructureDefinition'", StringComparison.Ordinal));
            Assert.Contains(result.Issues, i => i.Diagnostics.Contains("2 'ValueSet'", StringComparison.Ordinal));
            Assert.Contains(result.Issues, i => i.Diagnostics.Contains("'Patient'", StringComparison.Ordinal));
            Assert.Contains(result.Issues, i => i.Diagnostics.Contains("example.other.package", StringComparison.Ordinal));
            Assert.Equal(4, result.Issues.Count);
        }

        [Fact]
        public async Task GivenAHandlerRejectsItsSet_WhenInstalling_ThenNoPlanIsAppliedAndAllIssuesAreReported()
        {
            // Arrange
            IFhirPackageInstallPlan validPlan = CreatePlan("CodeSystem", "http://example.org/cs");
            IFhirPackageResourceHandler validHandler = CreateHandler("CodeSystem", validPlan);
            IFhirPackageResourceHandler rejectingHandler = Substitute.For<IFhirPackageResourceHandler>();
            rejectingHandler.ResourceType.Returns(SearchParameterType);
            rejectingHandler
                .ValidateAsync(Arg.Any<IReadOnlyList<FhirPackageEntry>>(), Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns<Task<IFhirPackageInstallPlan>>(_ => throw new FhirPackageException(new[] { Error("first"), Error("second") }));

            var package = new FhirPackage(
                Descriptor,
                new[] { Entry("package/CodeSystem-a.json", "CodeSystem"), Entry("package/SearchParameter-a.json", SearchParameterType) });

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateInstaller(validHandler, rejectingHandler).InstallAsync(package, CancellationToken.None));

            // Assert
            await validHandler.Received(1).ValidateAsync(Arg.Any<IReadOnlyList<FhirPackageEntry>>(), package, Arg.Any<CancellationToken>());
            await validPlan.DidNotReceive().ApplyAsync(Arg.Any<CancellationToken>());
            Assert.Equal(new[] { "first", "second" }, exception.Issues.Select(i => i.Diagnostics));
        }

        [Fact]
        public async Task GivenNoInstallableResources_WhenInstalling_ThenThePackageIsRejectedWithoutCallingHandlers()
        {
            // Arrange
            IFhirPackageResourceHandler handler = CreateHandler(SearchParameterType, CreatePlan(SearchParameterType));
            var package = new FhirPackage(
                Descriptor,
                new[] { Entry("package/StructureDefinition-x.json", "StructureDefinition") });

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateInstaller(handler).InstallAsync(package, CancellationToken.None));

            // Assert
            await handler.DidNotReceive().ValidateAsync(Arg.Any<IReadOnlyList<FhirPackageEntry>>(), Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>());
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("SearchParameter", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenCancellationDuringValidation_WhenInstalling_ThenNothingIsWritten()
        {
            // Arrange
            using var cancellationTokenSource = new CancellationTokenSource();
            IFhirPackageInstallPlan plan = CreatePlan(SearchParameterType, "http://example.org/sp/a");
            IFhirPackageResourceHandler handler = Substitute.For<IFhirPackageResourceHandler>();
            handler.ResourceType.Returns(SearchParameterType);
            handler
                .ValidateAsync(Arg.Any<IReadOnlyList<FhirPackageEntry>>(), Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cancellationTokenSource.Cancel();
                    return Task.FromResult(plan);
                });
            var package = new FhirPackage(Descriptor, new[] { Entry("package/SearchParameter-a.json", SearchParameterType) });

            // Act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateInstaller(handler).InstallAsync(package, cancellationTokenSource.Token));

            // Assert
            await plan.DidNotReceive().ApplyAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenTheWriteFails_WhenInstalling_ThenTheFailurePropagatesAndNoResultIsReturned()
        {
            // Arrange
            IFhirPackageInstallPlan plan = CreatePlan(SearchParameterType, "http://example.org/sp/a");
            plan.ApplyAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("write failed"));
            IFhirPackageResourceHandler handler = CreateHandler(SearchParameterType, plan);
            var package = new FhirPackage(Descriptor, new[] { Entry("package/SearchParameter-a.json", SearchParameterType) });

            // Act and assert
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateInstaller(handler).InstallAsync(package, CancellationToken.None));
            Assert.Equal("write failed", exception.Message);
        }

        [Fact]
        public void GivenTwoHandlersForTheSameResourceType_WhenCreatingTheInstaller_ThenItThrows()
        {
            // Arrange
            IFhirPackageResourceHandler first = CreateHandler(SearchParameterType, CreatePlan(SearchParameterType));
            IFhirPackageResourceHandler second = CreateHandler(SearchParameterType, CreatePlan(SearchParameterType));

            // Act and assert
            Assert.Throws<InvalidOperationException>(() => CreateInstaller(first, second));
        }

        private static FhirPackageInstaller CreateInstaller(params IFhirPackageResourceHandler[] handlers) =>
            new FhirPackageInstaller(handlers, NullLogger<FhirPackageInstaller>.Instance);

        private static IFhirPackageResourceHandler CreateHandler(string resourceType, IFhirPackageInstallPlan plan)
        {
            IFhirPackageResourceHandler handler = Substitute.For<IFhirPackageResourceHandler>();
            handler.ResourceType.Returns(resourceType);
            handler
                .ValidateAsync(Arg.Any<IReadOnlyList<FhirPackageEntry>>(), Arg.Any<FhirPackage>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(plan));
            return handler;
        }

        private static IFhirPackageInstallPlan CreatePlan(string resourceType, params string[] urls)
        {
            IFhirPackageInstallPlan plan = Substitute.For<IFhirPackageInstallPlan>();
            plan.ResourceType.Returns(resourceType);
            plan.CanonicalUrls.Returns(urls);
            plan.ApplyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            return plan;
        }

        private static FhirPackageEntry Entry(string path, string resourceType) => new FhirPackageEntry(path, resourceType, "{}");

        private static OperationOutcomeIssue Error(string diagnostics) => new OperationOutcomeIssue("Error", "Invalid", diagnostics);
    }
}
