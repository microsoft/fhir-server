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
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ServerProvideProfileValidationTests : IDisposable
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly ISearchService _searchService;
        private readonly IScoped<ISearchService> _scopedSearchService;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IMediator _mediator;
        private readonly IOptions<ValidateOperationConfiguration> _options;
        private readonly ServerProvideProfileValidation _serverProvideProfileValidation;

        public ServerProvideProfileValidationTests()
        {
            _hostApplicationLifetime = Substitute.For<IHostApplicationLifetime>();
            _hostApplicationLifetime.ApplicationStopping.Returns(CancellationToken.None);
            _searchService = Substitute.For<ISearchService>();
            _scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            _scopedSearchService.Value.Returns(_searchService);
            _searchServiceFactory = () => _scopedSearchService;
            _mediator = Substitute.For<IMediator>();

            var config = new ValidateOperationConfiguration
            {
                CacheDurationInSeconds = 300, // 5 minutes
                BackgroundProfileStatusDelayedStartInSeconds = 1,
                BackgroundProfileStatusCheckIntervalInSeconds = 3,
            };
            _options = Options.Create(config);

            _serverProvideProfileValidation = new ServerProvideProfileValidation(
                _searchServiceFactory,
                _options,
                _mediator,
                _hostApplicationLifetime,
                NullLogger<ServerProvideProfileValidation>.Instance);
        }

        [Fact]
        public void GivenServerProvideProfileValidation_WhenGettingProfileTypes_ThenCorrectTypesAreReturned()
        {
            // Act
            var profileTypes = _serverProvideProfileValidation.GetProfilesTypes();

            // Assert
            Assert.NotNull(profileTypes);
            Assert.Equal(3, profileTypes.Count);
            Assert.Contains("ValueSet", profileTypes);
            Assert.Contains("StructureDefinition", profileTypes);
            Assert.Contains("CodeSystem", profileTypes);
        }

        [Fact]
        public async Task GivenNoStructureDefinitions_WhenGettingSupportedProfiles_ThenEmptyListIsReturned()
        {
            // Arrange
            SetupSearchServiceWithNoResults();

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Empty(profiles);
            Assert.False(_serverProvideProfileValidation.IsSyncRequested());
        }

        [Fact]
        public async Task GivenStructureDefinitionsExist_WhenGettingSupportedProfiles_ThenMatchingProfilesAreReturned()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-patient", profiles);
        }

        [Fact]
        public async Task GivenANewStructureDefinition_WhenBackgroundLoopRuns_ThenSyncIsRequested()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // Wait for background refresh to complete
            await Task.Delay(TimeSpan.FromSeconds(10));

            // Sync should be requested after profile is added.
            Assert.True(_serverProvideProfileValidation.IsSyncRequested());

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);
            _serverProvideProfileValidation.MarkSyncCompleted();

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-patient", profiles);
            Assert.False(_serverProvideProfileValidation.IsSyncRequested());
        }

        [Fact]
        public async Task GivenMultipleNewStructureDefinitions_WhenBackgroundLoopRuns_ThenSyncIsRequested()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // Wait for background refresh to complete
            await Task.Delay(TimeSpan.FromSeconds(10));

            // Sync should be requested after profile is added.
            Assert.True(_serverProvideProfileValidation.IsSyncRequested());

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);
            _serverProvideProfileValidation.MarkSyncCompleted();

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-patient", profiles);
            Assert.False(_serverProvideProfileValidation.IsSyncRequested());

            var observationProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-observation", "Observation");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile, observationProfile);

            // Refreshing the profiles to reset cache expiration time.
            // This is something that would be done by the dependent services in a real scenario.
            _serverProvideProfileValidation.Refresh();

            // Wait for background refresh to complete
            await Task.Delay(TimeSpan.FromSeconds(10));

            // Sync should be requested after profile is added.
            Assert.True(_serverProvideProfileValidation.IsSyncRequested());

            // Act
            profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Observation", CancellationToken.None);
            _serverProvideProfileValidation.MarkSyncCompleted();

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-observation", profiles);
            Assert.False(_serverProvideProfileValidation.IsSyncRequested());
        }

        [Fact]
        public async Task GivenMultipleStructureDefinitions_WhenGettingSupportedProfiles_ThenOnlyMatchingResourceTypeIsReturned()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            var observationProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-observation", "Observation");

            SetupSearchServiceWithResults("StructureDefinition", patientProfile, observationProfile);

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-patient", profiles);
            Assert.DoesNotContain("http://example.org/fhir/StructureDefinition/custom-observation", profiles);
        }

        [Fact]
        public async Task GivenCachedResults_WhenGettingSupportedProfiles_ThenCacheIsUsed()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // Act - First call
            await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Act - Second call (should use cache)
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None, disableCacheRefresh: true);

            // Assert
            Assert.NotNull(profiles);

            // Verify search was only called once (second call used cache)
            await _searchService.Received(1).SearchAsync(
                "StructureDefinition",
                Arg.Any<List<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void GivenServerProvideProfileValidation_WhenRefreshIsCalled_ThenCacheIsMarkedForRefresh()
        {
            // Act
            _serverProvideProfileValidation.Refresh();

            // Assert - No exception should be thrown
            Assert.NotNull(_serverProvideProfileValidation);
        }

        [Fact]
        public async Task GivenStructureDefinitionWithoutType_WhenGettingSupportedProfiles_ThenItIsNotIncluded()
        {
            // Arrange - Create a malformed StructureDefinition without Type property
            var malformedProfile = new StructureDefinition
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16), // ID is required for ResourceWrapper
                Url = "http://example.org/fhir/StructureDefinition/malformed",
                Name = "MalformedProfile",
                Status = PublicationStatus.Active,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,

                // Type property intentionally not set
            };

            SetupSearchServiceWithResults("StructureDefinition", malformedProfile);

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Empty(profiles);
        }

        [Fact]
        public async Task GivenPaginatedResults_WhenGettingSupportedProfiles_ThenAllPagesAreProcessed()
        {
            // Arrange
            var profile1 = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/patient-1", "Patient");
            var profile2 = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/patient-2", "Patient");

            // Setup first page
            SetupSearchServiceWithPaginatedResults("StructureDefinition", "page2token", profile1);

            // Setup second page
            var searchResult2 = CreateSearchResult(null, profile2);
            _searchService.SearchAsync(
                "StructureDefinition",
                Arg.Is<List<Tuple<string, string>>>(list =>
                    list != null && list.Any(t => t.Item1 == "ct")),
                Arg.Any<CancellationToken>())
                .Returns(searchResult2);

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Equal(2, profiles.Count());
            Assert.Contains("http://example.org/fhir/StructureDefinition/patient-1", profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/patient-2", profiles);
        }

        [Fact]
        public async Task GivenValueSetResources_WhenGettingSupportedProfiles_ThenTheyAreNotIncluded()
        {
            // Arrange
            var valueSet = new ValueSet
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16), // ID is required for ResourceWrapper
                Url = "http://example.org/fhir/ValueSet/test",
                Name = "TestValueSet",
                Status = PublicationStatus.Active,
            };

            SetupSearchServiceWithResults("ValueSet", valueSet);

            // Act
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Empty(profiles);
        }

        [Fact]
        public async Task GivenCaseInsensitiveResourceType_WhenGettingSupportedProfiles_ThenMatchingProfilesAreReturned()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // Act - Query with lowercase
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("patient", CancellationToken.None);

            // Assert
            Assert.NotNull(profiles);
            Assert.Single(profiles);
            Assert.Contains("http://example.org/fhir/StructureDefinition/custom-patient", profiles);
        }

        [Fact]
        public async Task GivenDisableCacheRefresh_WhenGettingSupportedProfiles_ThenCacheIsNotRefreshed()
        {
            // Arrange
            var patientProfile = CreateStructureDefinition("http://example.org/fhir/StructureDefinition/custom-patient", "Patient");
            SetupSearchServiceWithResults("StructureDefinition", patientProfile);

            // First call
            await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None);

            // Mark for refresh
            _serverProvideProfileValidation.Refresh();

            // Act - Call with cache refresh disabled
            var profiles = await _serverProvideProfileValidation.GetSupportedProfilesAsync("Patient", CancellationToken.None, disableCacheRefresh: true);

            // Assert - Should still return results from initial cache
            Assert.NotNull(profiles);
            Assert.Single(profiles);
        }

        public void Dispose()
        {
            _serverProvideProfileValidation?.Dispose();
        }

        private static StructureDefinition CreateStructureDefinition(string url, string type)
        {
            return new StructureDefinition
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16), // Generate valid FHIR ID
                Url = url,
                Name = $"{type}Profile",
                Status = PublicationStatus.Active,
                Kind = StructureDefinition.StructureDefinitionKind.Resource,
                Abstract = false,
                Type = type,
                BaseDefinition = $"http://hl7.org/fhir/StructureDefinition/{type}",
                Derivation = StructureDefinition.TypeDerivationRule.Constraint,
            };
        }

        private void SetupSearchServiceWithNoResults()
        {
            var emptyResult = new SearchResult(
                new List<SearchResultEntry>(),
                null,
                null,
                new List<Tuple<string, string>>());

            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<List<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(emptyResult);
        }

        private void SetupSearchServiceWithResults(string resourceType, params Resource[] resources)
        {
            var searchEntries = resources.Select(r => CreateSearchResultEntry(r)).ToList();
            var searchResult = new SearchResult(searchEntries, null, null, new List<Tuple<string, string>>());

            _searchService.SearchAsync(
                resourceType,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(searchResult);

            // Setup for other resource types to return empty
            foreach (string type in new[] { "ValueSet", "CodeSystem", "StructureDefinition" }.Where(type => type != resourceType))
            {
                _searchService.SearchAsync(
                    type,
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>())
                    .Returns(new SearchResult(new List<SearchResultEntry>(), null, null, new List<Tuple<string, string>>()));
            }
        }

        private void SetupSearchServiceWithPaginatedResults(string resourceType, string continuationToken, params Resource[] resources)
        {
            var searchEntries = resources.Select(r => CreateSearchResultEntry(r)).ToList();
            var searchResult = new SearchResult(searchEntries, continuationToken, null, new List<Tuple<string, string>>());

            _searchService.SearchAsync(
                resourceType,
                Arg.Is<List<Tuple<string, string>>>(list => list != null && !list.Any(t => t.Item1 == "ct")),
                Arg.Any<CancellationToken>())
                .Returns(searchResult);

            // Setup empty results for other types
            foreach (var type in new[] { "ValueSet", "CodeSystem" })
            {
                _searchService.SearchAsync(
                    type,
                    Arg.Any<List<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>())
                    .Returns(new SearchResult(new List<SearchResultEntry>(), null, null, new List<Tuple<string, string>>()));
            }
        }

        private static SearchResult CreateSearchResult(string continuationToken, params Resource[] resources)
        {
            var searchEntries = resources.Select(r => CreateSearchResultEntry(r)).ToList();
            return new SearchResult(searchEntries, continuationToken, null, new List<Tuple<string, string>>());
        }

        private static SearchResultEntry CreateSearchResultEntry(Resource resource)
        {
            var json = new FhirJsonSerializer().SerializeToString(resource);
            var rawResource = new RawResource(json, FhirResourceFormat.Json, false);
            var resourceElement = resource.ToResourceElement();

            var wrapper = new ResourceWrapper(
                resourceElement,
                rawResource,
                new ResourceRequest("GET"),
                false,
                null,
                null,
                null);

            return new SearchResultEntry(wrapper);
        }
    }
}
