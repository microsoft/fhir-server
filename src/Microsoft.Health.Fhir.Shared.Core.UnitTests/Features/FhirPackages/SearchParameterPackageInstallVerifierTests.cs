// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParameterStatus = Microsoft.Health.Fhir.Core.Features.Search.Registry.SearchParameterStatus;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class SearchParameterPackageInstallVerifierTests
    {
        private const string UrlA = "https://example.org/SearchParameter/a";
        private const string UrlB = "https://example.org/SearchParameter/b";
        private static readonly DateTimeOffset LastUpdated = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        private static readonly FhirJsonSerializer Serializer = new FhirJsonSerializer();

        private readonly ISearchService _search = Substitute.For<ISearchService>();
        private readonly ISearchParameterOperations _operations = Substitute.For<ISearchParameterOperations>();
        private readonly ISearchParameterStatusManager _statuses = Substitute.For<ISearchParameterStatusManager>();
        private readonly ISearchParameterDefinitionManager _definitions = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IModelInfoProvider _model = new VersionSpecificModelInfoProvider();
        private readonly SearchParameterPackageInstallVerifier _verifier;

        public SearchParameterPackageInstallVerifierTests()
        {
            _operations.SearchParamLastUpdated.Returns(LastUpdated);
            _definitions.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(call =>
                {
                    call[1] = new SearchParameterInfo("test", "test")
                    {
                        SearchParameterStatus = SearchParameterStatus.Enabled,
                        IsSearchable = true,
                    };
                    return true;
                });
            _verifier = new SearchParameterPackageInstallVerifier(
                _search.CreateMockScopeProvider(),
                _operations,
                _statuses,
                _definitions,
                _model);
        }

        [Fact]
        public async Task GivenAResourceWithServerAssignedMetadata_WhenInspecting_ThenItMatchesThePackage()
        {
            // Arrange
            SearchParameter expected = Parameter(UrlA, "a");
            SearchParameter persisted = (SearchParameter)expected.DeepCopy();
            persisted.Id = "server-assigned";
            persisted.Meta = new Meta { VersionId = "3", LastUpdated = LastUpdated };
            SetupStatuses(Status(UrlA));
            SetupResults(Result(null, persisted));

            // Act
            FhirPackageInstallInspection inspection = await _verifier.InspectAsync(Package(expected), CancellationToken.None);

            // Assert
            Assert.Empty(inspection.MissingEntries);
            Assert.Equal(new[] { UrlA }, inspection.CanonicalUrls);
            Assert.Equal(LastUpdated, inspection.LastUpdated);
        }

        [Fact]
        public async Task GivenSomeMatchingResources_WhenInspecting_ThenOnlyMissingEntriesAreReturned()
        {
            // Arrange
            SearchParameter first = Parameter(UrlA, "a");
            SearchParameter second = Parameter(UrlB, "b");
            var package = Package(first, second);
            SetupStatuses(Status(UrlA));
            SetupResults(Result(null, first));

            // Act
            FhirPackageInstallInspection inspection = await _verifier.InspectAsync(package, CancellationToken.None);

            // Assert
            Assert.Same(package.ResourceEntries[1], Assert.Single(inspection.MissingEntries));
            Assert.Equal(new[] { UrlA, UrlB }, inspection.CanonicalUrls);
        }

        [Fact]
        public async Task GivenAnExistingDifferentResource_WhenInspecting_ThenItIsAConflict()
        {
            // Arrange
            SearchParameter expected = Parameter(UrlA, "a");
            SearchParameter persisted = Parameter(UrlA, "different");
            SetupStatuses(Status(UrlA));
            SetupResults(Result(null, persisted));

            // Act
            FhirPackageWriteConflictException exception = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => _verifier.InspectAsync(Package(expected), CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains(UrlA, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(SearchParameterStatus.Disabled)]
        [InlineData(SearchParameterStatus.Supported)]
        public async Task GivenAMatchingResourceWithANonEnabledStatus_WhenInspecting_ThenItIsAConflict(SearchParameterStatus status)
        {
            // Arrange
            SearchParameter parameter = Parameter(UrlA, "a");
            SetupStatuses(Status(UrlA, status));
            SetupResults(Result(null, parameter));

            // Act
            FhirPackageWriteConflictException exception = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => _verifier.InspectAsync(Package(parameter), CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains("status", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GivenAStatusButNoResource_WhenInspecting_ThenItIsAConflictRatherThanAWrite()
        {
            // Arrange
            SetupStatuses(Status(UrlA));
            SetupResults(Result(null));

            // Act
            FhirPackageWriteConflictException exception = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => _verifier.InspectAsync(Package(Parameter(UrlA, "a")), CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
        }

        [Fact]
        public async Task GivenTheLocalCacheDidNotLoadTheParameter_WhenInspecting_ThenTheFailureIsRetryable()
        {
            // Arrange
            SearchParameter parameter = Parameter(UrlA, "a");
            SetupStatuses(Status(UrlA));
            SetupResults(Result(null, parameter));
            _definitions.TryGetSearchParameter(Arg.Any<string>(), out Arg.Any<SearchParameterInfo>()).Returns(false);

            // Act
            FhirPackageWriteConflictException exception = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => _verifier.InspectAsync(Package(parameter), CancellationToken.None));

            // Assert
            Assert.True(exception.IsRetryable);
        }

        [Fact]
        public async Task GivenDuplicateResourcesOnDifferentPages_WhenInspecting_ThenTheyAreRejected()
        {
            // Arrange
            SearchParameter parameter = Parameter(UrlA, "a");
            SetupStatuses(Status(UrlA));
            SetupResults(Result("next-page", parameter), Result(null, parameter));

            // Act
            FhirPackageWriteConflictException exception = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(
                () => _verifier.InspectAsync(Package(parameter), CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains("more than one", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAnUrlContainingSearchSeparators_WhenInspecting_ThenTheUrlIsEscaped()
        {
            // Arrange
            const string url = "https://example.org/SearchParameter/a,b";
            SetupStatuses();
            SetupResults(Result(null));

            // Act
            await _verifier.InspectAsync(Package(Parameter(url, "a")), CancellationToken.None);

            // Assert
            await _search.Received(1).SearchAsync(
                KnownResourceTypes.SearchParameter,
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(values => values.Any(v => v.Item1 == "url" && v.Item2 == @"https://example.org/SearchParameter/a\,b")),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<DataActions>());
        }

        private static SearchParameter Parameter(string url, string code) => new SearchParameter
        {
            Url = url,
            Name = code,
            Code = code,
            Status = PublicationStatus.Active,
            Description = "test",
            Type = SearchParamType.Token,
            Expression = "Patient.identifier",
#if Stu3 || R4 || R4B
            Base = new List<ResourceType?> { ResourceType.Patient },
#else
            Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient },
#endif
        };

        private static FhirPackage Package(params SearchParameter[] parameters) => new FhirPackage(
            new FhirPackageDescriptor("example.package", "1.0.0", "sha512-test"),
            parameters.Select((p, index) => new FhirPackageEntry($"package/{index}.json", KnownResourceTypes.SearchParameter, Serializer.SerializeToString(p))).ToArray());

        private static ResourceSearchParameterStatus Status(string url, SearchParameterStatus status = SearchParameterStatus.Enabled) =>
            new ResourceSearchParameterStatus { Uri = new Uri(url), Status = status, LastUpdated = LastUpdated };

        private void SetupStatuses(params ResourceSearchParameterStatus[] statuses) =>
            _statuses.GetAllSearchParameterStatuses(Arg.Any<CancellationToken>()).Returns(statuses);

        private void SetupResults(params SearchResult[] results) =>
            _search.SearchAsync(
                    KnownResourceTypes.SearchParameter,
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<ResourceVersionType>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<DataActions>())
                .Returns(results[0], results.Skip(1).ToArray());

        private SearchResult Result(string continuationToken, params SearchParameter[] parameters)
        {
            var entries = parameters.Select(parameter =>
            {
                var persisted = (SearchParameter)parameter.DeepCopy();
                persisted.Id ??= "server-assigned";
                var raw = new RawResource(Serializer.SerializeToString(persisted), FhirResourceFormat.Json, false);
                var wrapper = new ResourceWrapper(
                    new ResourceElement(raw.ToITypedElement(_model)),
                    raw,
                    new ResourceRequest("POST"),
                    false,
                    null,
                    null,
                    null);
                return new SearchResultEntry(wrapper);
            });
            return new SearchResult(entries, continuationToken, null, Array.Empty<Tuple<string, string>>());
        }
    }
}
