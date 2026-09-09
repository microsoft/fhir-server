// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        private static VectorSearchParameterResolver CreateResolver(ISearchParameterDefinitionManager definitionManager)
        {
            return new VectorSearchParameterResolver(definitionManager);
        }

        private static SearchParameterInfo CreateVectorSearchParameter(
            Uri canonicalUri,
            bool includeVectorConfig = true,
            string definitionStatus = "active",
            bool isSearchable = true,
            SearchParameterStatus searchParameterStatus = SearchParameterStatus.Enabled)
        {
            var searchParameter = new SearchParameterInfo(
                name: "ObservationNoteVector",
                code: "note-vector",
                searchParamType: SearchParamType.Special,
                url: canonicalUri,
                expression: "Observation.note.text",
                baseResourceTypes: new[] { "Observation" },
                vectorConfig: includeVectorConfig ? new VectorSearchParameterConfig() : null,
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
