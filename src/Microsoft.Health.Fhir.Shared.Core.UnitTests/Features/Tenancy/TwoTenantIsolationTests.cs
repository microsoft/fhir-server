// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Verifies that two tenant-scoped search parameter managers built from one shared source keep
    /// their per-manager mutable state isolated while exposing the same shared system definitions.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TwoTenantIsolationTests
    {
        private static readonly TenantId Contoso = new("contoso");
        private static readonly TenantId Fabrikam = new("fabrikam");

        private readonly ISearchParameterDefinitionSource _sharedSource = new EmbeddedSearchParameterDefinitionSource(ModelInfoProvider.Instance);

        [Fact]
        public void GivenTwoDefinitionManagersFromOneSource_WhenOneUpdatesItsHashMap_ThenTheOtherIsUnaffected()
        {
            // Arrange
            SearchParameterDefinitionManager contosoManager = CreateDefinitionManager();
            SearchParameterDefinitionManager fabrikamManager = CreateDefinitionManager();

            // Act
            contosoManager.UpdateSearchParameterHashMap(new Dictionary<string, string> { { "Patient", "contoso-hash" } });
            fabrikamManager.UpdateSearchParameterHashMap(new Dictionary<string, string> { { "Patient", "fabrikam-hash" } });

            // Assert
            Assert.Equal("contoso-hash", contosoManager.SearchParameterHashMap["Patient"]);
            Assert.Equal("fabrikam-hash", fabrikamManager.SearchParameterHashMap["Patient"]);
        }

        [Fact]
        public void GivenTwoDefinitionManagersFromOneSource_WhenBothAreConstructed_ThenEachExposesTheSameParameterSet()
        {
            // Arrange & Act
            SearchParameterDefinitionManager contosoManager = CreateDefinitionManager();
            SearchParameterDefinitionManager fabrikamManager = CreateDefinitionManager();

            HashSet<Uri> contosoUrls = contosoManager.AllSearchParameters.Select(p => p.Url).ToHashSet();
            HashSet<Uri> fabrikamUrls = fabrikamManager.AllSearchParameters.Select(p => p.Url).ToHashSet();

            // Assert
            Assert.NotSame(contosoManager, fabrikamManager);
            Assert.NotEmpty(contosoUrls);
            Assert.True(
                contosoUrls.SetEquals(fabrikamUrls),
                "Both managers were built from the same ISearchParameterDefinitionSource and must expose the same parameter set.");
        }

        [Fact]
        public void GivenOneInstanceConfiguration_WhenTwoTenantsInitializeBaseUris_ThenEachTenantSeesItsOwn()
        {
            // Arrange
            ITenantContextAccessor tenantContextAccessor = new TenantContextAccessor();
            var configuration = new FhirServerInstanceConfiguration(tenantContextAccessor);

            // Act
            tenantContextAccessor.SetCurrent(Contoso);
            bool contosoInitialized = configuration.InitializeBaseUri("https://contoso.example.org/");

            tenantContextAccessor.SetCurrent(Fabrikam);
            bool fabrikamInitialized = configuration.InitializeBaseUri("https://fabrikam.example.org/");

            // Assert
            Assert.True(contosoInitialized);
            Assert.True(fabrikamInitialized);

            tenantContextAccessor.SetCurrent(Contoso);
            Assert.Equal(new Uri("https://contoso.example.org/"), configuration.BaseUri);

            tenantContextAccessor.SetCurrent(Fabrikam);
            Assert.Equal(new Uri("https://fabrikam.example.org/"), configuration.BaseUri);
        }

        private SearchParameterDefinitionManager CreateDefinitionManager()
        {
            return new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _sharedSource,
                Substitute.For<IMediator>(),
                Substitute.For<ISearchService>().CreateMockScopeProvider(),
                Substitute.For<ISearchParameterComparer<SearchParameterInfo>>(),
                Substitute.For<ISearchParameterStatusDataStore>().CreateMockScopeProvider(),
                Substitute.For<IFhirDataStore>().CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);
        }
    }
}
