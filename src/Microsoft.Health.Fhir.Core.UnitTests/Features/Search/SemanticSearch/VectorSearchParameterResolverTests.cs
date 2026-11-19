// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class VectorSearchParameterResolverTests
    {
        private static readonly Uri VectorCanonical = new Uri("https://example.org/fhir/SearchParameter/observation-note-vector");

        [Fact]
        public void GivenSearchableVectorSearchParameter_WhenResolvingResourceType_ThenOnlyVectorDefinitionIsReturned()
        {
            // Arrange
            SearchParameterInfo vectorSearchParameter = CreateVectorSearchParameter(VectorCanonical);
            var ordinarySearchParameter = new SearchParameterInfo(
                "ObservationCode",
                "code",
                SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { ordinarySearchParameter, vectorSearchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            IReadOnlyList<SearchParameterInfo> results = resolver.GetSearchParameters("Observation");

            // Assert
            Assert.Collection(results, result => Assert.Same(vectorSearchParameter, result));
        }

        [Fact]
        public void GivenUnregisteredCanonical_WhenResolved_ThenSearchParameterNotSupportedExceptionIsThrown()
        {
            // Arrange
            var unregisteredCanonical = new Uri("https://example.org/fhir/SearchParameter/unregistered-vector");
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            Action resolve = () => resolver.GetSearchParameter(unregisteredCanonical);

            // Assert
            Assert.Throws<SearchParameterNotSupportedException>(resolve);
        }

        [Fact]
        public void GivenVectorSearchParameterThatIsNotSearchable_WhenResolvingResourceType_ThenDefinitionIsSkipped()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, isSearchable: false);
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { searchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            IReadOnlyList<SearchParameterInfo> results = resolver.GetSearchParameters("Observation");

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public void GivenSupportedVectorSearchParameterThatIsNotSearchable_WhenResolvingForIndexing_ThenDefinitionIsReturned()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                isSearchable: false,
                searchParameterStatus: SearchParameterStatus.Supported);
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { searchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            IReadOnlyList<SearchParameterInfo> results = resolver.GetIndexingSearchParameters("Observation");

            // Assert
            Assert.Collection(results, result => Assert.Same(searchParameter, result));
        }

        [Fact]
        public void GivenNonSearchableVectorSearchParameterOutsideSupportedState_WhenResolvingForIndexing_ThenDefinitionIsSkipped()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                isSearchable: false,
                searchParameterStatus: SearchParameterStatus.Disabled);
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { searchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            IReadOnlyList<SearchParameterInfo> results = resolver.GetIndexingSearchParameters("Observation");

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public void GivenSearchParameterWithoutVectorExtension_WhenResolved_ThenResolutionFails()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, includeVectorConfig: false);
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            ConfigureRegisteredDefinition(definitionManager, searchParameter);
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            Action resolve = () => resolver.GetSearchParameter(VectorCanonical);

            // Assert
            Assert.Throws<InvalidOperationException>(resolve);
        }

        [Fact]
        public void GivenVectorSearchParameterThatIsNotActive_WhenResolved_ThenResolutionFails()
        {
            // Arrange
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, definitionStatus: "draft");
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            ConfigureRegisteredDefinition(definitionManager, searchParameter);
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            // Act
            Action resolve = () => resolver.GetSearchParameter(VectorCanonical);

            // Assert
            Assert.Throws<InvalidOperationException>(resolve);
        }

        [Fact]
        public void GivenOverlapOnlyOverrideSmallerThanGlobalChunkSize_WhenResolvingForIndexing_ThenDefinitionIsReturned()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                vectorConfig: new VectorSearchParameterConfig { ChunkOverlapTokens = 200 });
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { searchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            IReadOnlyList<SearchParameterInfo> results = resolver.GetIndexingSearchParameters("Observation");

            Assert.Collection(results, result => Assert.Same(searchParameter, result));
        }

        [Fact]
        public void GivenOverlapOnlyOverrideEqualToGlobalChunkSize_WhenResolvingForIndexing_ThenDefinitionIsSkipped()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                vectorConfig: new VectorSearchParameterConfig { ChunkOverlapTokens = 800 });
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters("Observation").Returns(new[] { searchParameter });
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            IReadOnlyList<SearchParameterInfo> results = resolver.GetIndexingSearchParameters("Observation");

            Assert.Empty(results);
        }

        [Fact]
        public void GivenChunkSizeOnlyOverrideNotLargerThanGlobalOverlap_WhenResolved_ThenResolutionFails()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                vectorConfig: new VectorSearchParameterConfig { ChunkSizeTokens = 100 });
            ISearchParameterDefinitionManager definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            ConfigureRegisteredDefinition(definitionManager, searchParameter);
            VectorSearchParameterResolver resolver = CreateResolver(definitionManager);

            Action resolve = () => resolver.GetSearchParameter(VectorCanonical);

            Assert.Throws<InvalidOperationException>(resolve);
        }

        [Fact]
        public void GivenServableVectorSearchParameterDefinition_WhenValidatingDefinition_ThenDefinitionIsValid()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical);

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.True(result);
            Assert.Null(errorMessage);
        }

        [Fact]
        public void GivenVectorSearchParameterDefinitionThatIsNotSpecial_WhenValidatingDefinition_ThenDefinitionIsInvalid()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, searchParamType: SearchParamType.String);

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains("FHIR type 'special'", errorMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("draft")]
        [InlineData("retired")]
        public void GivenVectorSearchParameterDefinitionThatIsNotActive_WhenValidatingDefinition_ThenDefinitionIsInvalid(string definitionStatus)
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, definitionStatus: definitionStatus);

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains("publication status 'active'", errorMessage);
        }

        [Fact]
        public void GivenVectorSearchParameterDefinitionWithoutBaseResourceTypes_WhenValidatingDefinition_ThenDefinitionIsInvalid()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, baseResourceTypes: Array.Empty<string>());

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains("base resource type", errorMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public void GivenVectorSearchParameterDefinitionWithoutAnExpression_WhenValidatingDefinition_ThenDefinitionIsInvalid(string expression)
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, expression: expression);

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains("must declare an expression", errorMessage);
        }

        [Fact]
        public void GivenSearchParameterDefinitionWithoutVectorExtension_WhenValidatingDefinition_ThenDefinitionIsInvalid()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(VectorCanonical, includeVectorConfig: false);

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains(VectorSearchParameterConfig.ExtensionUrl, errorMessage);
        }

        [Fact]
        public void GivenVectorSearchParameterDefinitionWithInvalidEffectiveChunkSettings_WhenValidatingDefinition_ThenDefinitionIsInvalid()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter(
                VectorCanonical,
                vectorConfig: new VectorSearchParameterConfig { ChunkOverlapTokens = 800 });

            bool result = VectorSearchParameterResolver.TryValidateDefinition(searchParameter, new VectorSearchConfiguration(), out string errorMessage);

            Assert.False(result);
            Assert.Contains("chunk overlap", errorMessage);
        }

        private static VectorSearchParameterResolver CreateResolver(ISearchParameterDefinitionManager definitionManager)
        {
            return new VectorSearchParameterResolver(
                definitionManager,
                Options.Create(new VectorSearchConfiguration()));
        }

        private static SearchParameterInfo CreateVectorSearchParameter(
            Uri canonicalUri,
            bool includeVectorConfig = true,
            string definitionStatus = "active",
            bool isSearchable = true,
            SearchParameterStatus searchParameterStatus = SearchParameterStatus.Enabled,
            VectorSearchParameterConfig vectorConfig = null,
            SearchParamType searchParamType = SearchParamType.Special,
            string expression = "Observation.note.text",
            IReadOnlyList<string> baseResourceTypes = null)
        {
            var searchParameter = new SearchParameterInfo(
                name: "ObservationNoteVector",
                code: "note-vector",
                searchParamType: searchParamType,
                url: canonicalUri,
                expression: expression,
                baseResourceTypes: baseResourceTypes ?? new[] { "Observation" },
                vectorConfig: includeVectorConfig ? vectorConfig ?? new VectorSearchParameterConfig() : null,
                definitionStatus: definitionStatus);

            searchParameter.IsSearchable = isSearchable;
            searchParameter.IsSupported = true;
            searchParameter.SearchParameterStatus = searchParameterStatus;
            return searchParameter;
        }

        private static void ConfigureRegisteredDefinition(
            ISearchParameterDefinitionManager definitionManager,
            SearchParameterInfo searchParameter)
        {
            definitionManager.TryGetSearchParameter(searchParameter.Url.OriginalString, true, out Arg.Any<SearchParameterInfo>())
                .Returns(callInfo =>
                {
                    callInfo[2] = searchParameter;
                    return true;
                });
        }
    }
}
