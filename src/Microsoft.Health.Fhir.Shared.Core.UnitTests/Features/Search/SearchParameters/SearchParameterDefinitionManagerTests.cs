// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterDefinitionManagerTests : IAsyncLifetime
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastUpdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";
        private static readonly string ResourceTest = "http://hl7.org/fhir/SearchParameter/Resource-test";

        private readonly SearchParameterStatusManager _manager;
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;
        private readonly SearchParameterInfo[] _searchParameterInfos;
        private readonly SearchParameterInfo _queryParameter;
        private readonly SearchParameterInfo _testSearchParamInfo;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IFhirRequestContext _fhirRequestContext = new DefaultFhirRequestContext();
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();

        public SearchParameterDefinitionManagerTests()
        {
            _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            _mediator = Substitute.For<IMediator>();
            _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            _searchService = Substitute.For<ISearchService>();
            _fhirDataStore = Substitute.For<IFhirDataStore>();

            _searchService = Substitute.For<ISearchService>();
            var mockScopeProvider = _searchService.CreateMockScopeProvider();
            var mockStatusDataStoreScopeProvider = _searchParameterStatusDataStore.CreateMockScopeProvider();
            var mockFhirDataStoreProvider = _fhirDataStore.CreateMockScopeProvider();

            _searchParameterComparer = Substitute.For<ISearchParameterComparer<SearchParameterInfo>>();
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _mediator,
                mockScopeProvider,
                _searchParameterComparer,
                mockStatusDataStoreScopeProvider,
                mockFhirDataStoreProvider,
                NullLogger<SearchParameterDefinitionManager>.Instance);

            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _manager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Disabled,
                        Uri = new Uri(ResourceProfile),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Supported,
                        Uri = new Uri(ResourceSecurity),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri("http://test/Patient-preexisting2"),
                    },
                });

            _queryParameter = new SearchParameterInfo("_query", "_query", SearchParamType.Token, new Uri(ResourceQuery), baseResourceTypes: new List<string>() { "Patient" });
            _searchParameterInfos = new[]
            {
                new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri(ResourceId)),
                new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Token, new Uri(ResourceLastUpdated)),
                new SearchParameterInfo("_profile", "_profile", SearchParamType.Token, new Uri(ResourceProfile)),
                new SearchParameterInfo("_security", "_security", SearchParamType.Token, new Uri(ResourceSecurity)),
                _queryParameter,
            };

            _testSearchParamInfo = new SearchParameterInfo("_test", "_test", SearchParamType.Special, new Uri(ResourceTest));

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Any<SearchParameterInfo>())
                .Returns((false, false));

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is(_searchParameterInfos[4]))
                .Returns((true, false));

            var searchParameterDataStoreValidator = Substitute.For<IDataStoreSearchParameterValidator>();
            searchParameterDataStoreValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(true, null);

            var searchService = Substitute.For<ISearchService>();

            _searchParameterOperations = new SearchParameterOperations(
                _manager,
                _searchParameterDefinitionManager,
                ModelInfoProvider.Instance,
                _searchParameterSupportResolver,
                searchParameterDataStoreValidator,
                () => searchService.CreateMockScope(),
                NullLogger<SearchParameterOperations>.Instance);
        }

        public async ValueTask InitializeAsync()
        {
            await _searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        [Fact]
        public async Task GivenSupportedParams_WhenGettingSupported_ThenSupportedParamsReturned()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);
            var supportedDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);
            var paramList = supportedDefinitionManager.GetSearchParametersRequiringReindexing();

            Assert.Collection(
                paramList,
                p =>
                {
                    Assert.True(p.IsSupported);
                    Assert.False(p.IsSearchable);
                },
                p2 =>
                {
                    Assert.True(p2.IsSupported);
                    Assert.False(p2.IsSearchable);
                });
        }

        [Fact]
        public async Task GivenSearchableParams_WhenGettingSearchable_ThenCorrectParamsReturned()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);
            var searchableDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);
            var paramList = searchableDefinitionManager.AllSearchParameters;

            Assert.Collection(
                paramList,
                pType =>
                {
                    // _type (ResourceTypeSearchParameter) is always searchable/supported,
                    // even without a status store entry.
                    Assert.Equal("_type", pType.Code);
                    Assert.True(pType.IsSupported);
                    Assert.True(pType.IsSearchable);
                },
                p =>
                {
                    Assert.True(p.IsSupported);
                    Assert.True(p.IsSearchable);
                },
                p2 =>
                {
                    Assert.True(p2.IsSupported);
                    Assert.True(p2.IsSearchable);
                });
        }

        [Fact]
        public async Task GivenContextToIncludePatialIndexedParams_WhenGettingSearchable_ThenCorrectParamsReturned()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);
            _fhirRequestContext.IncludePartiallyIndexedSearchParams = true;
            var searchableDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);
            var paramList = searchableDefinitionManager.AllSearchParameters.OrderBy(p => p.Code);

            Assert.Collection(
                paramList,
                p =>
                {
                    Assert.True(p.IsSupported);
                    Assert.True(p.IsSearchable);
                },
                p2 =>
                {
                    Assert.True(p2.IsSupported);
                    Assert.True(p2.IsSearchable);
                },
                p3 =>
                {
                    Assert.True(p3.IsSupported);
                    Assert.False(p3.IsSearchable);
                },
                p4 =>
                {
                    Assert.True(p4.IsSupported);
                    Assert.False(p4.IsSearchable);
                },
                pType =>
                {
                    // _type (ResourceTypeSearchParameter) is always searchable/supported.
                    Assert.Equal("_type", pType.Code);
                    Assert.True(pType.IsSupported);
                    Assert.True(pType.IsSearchable);
                });

            _fhirRequestContext.IncludePartiallyIndexedSearchParams = false;
        }

        [Fact]
        public async Task GivenNoHeaderForPatiallyIndexedParams_WhenSearchingSupportedParameterByName_ThenExceptionThrown()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);
            _fhirRequestContext.IncludePartiallyIndexedSearchParams = false;
            var searchableDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);

            Assert.Throws<SearchParameterNotSupportedException>(() => searchableDefinitionManager.GetSearchParameter(ResourceSecurity));

            _fhirRequestContext.IncludePartiallyIndexedSearchParams = false;
        }

        [Fact]
        public async Task GivenHeaderToIncludePatialIndexedParams_WhenSearchingSupportedParameterByName_ThenSupportedParamsReturned()
        {
            await _manager.EnsureInitializedAsync(CancellationToken.None);
            _fhirRequestContext.IncludePartiallyIndexedSearchParams = true;
            var searchableDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, _fhirRequestContextAccessor);

            var param = searchableDefinitionManager.GetSearchParameter(ResourceSecurity);
            SearchParameterInfo expectedSearchParam = _searchParameterInfos[3];
            ValidateSearchParam(expectedSearchParam, param);

            _fhirRequestContext.IncludePartiallyIndexedSearchParams = false;
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenGettingExistingSearchParameterByName_CorrectParameterIsReturned()
        {
            SearchParameterInfo expectedSearchParam = _searchParameterInfos[0];
            SearchParameterInfo actualSearchParam = _searchParameterDefinitionManager.GetSearchParameter(
                "SearchParameter",
                expectedSearchParam.Code);

            ValidateSearchParam(expectedSearchParam, actualSearchParam);
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenGettingNonexistentSearchParameterByName_ExceptionIsThrown()
        {
            Assert.Throws<SearchParameterNotSupportedException>(() => _searchParameterDefinitionManager.GetSearchParameter(
                "SearchParameter",
                _testSearchParamInfo.Code));
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenTryingToGetExistingSearchParameter_ReturnsTrue()
        {
            SearchParameterInfo expectedSearchParam = _searchParameterInfos[0];
            Assert.True(_searchParameterDefinitionManager.TryGetSearchParameter(
                "SearchParameter",
                expectedSearchParam.Code,
                out SearchParameterInfo actualSearchParam));

            ValidateSearchParam(expectedSearchParam, actualSearchParam);
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenTryingToGetNonexistentSearchParameter_ReturnsFalse()
        {
            Assert.False(_searchParameterDefinitionManager.TryGetSearchParameter(
                "SearchParameter",
                _testSearchParamInfo.Code,
                out SearchParameterInfo _));
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenGettingExistingSearchParameterByUrl_CorrectParameterIsReturned()
        {
            SearchParameterInfo expectedSearchParam = _searchParameterInfos[0];
            SearchParameterInfo actualSearchParam = _searchParameterDefinitionManager.GetSearchParameter(expectedSearchParam.Url.OriginalString);

            ValidateSearchParam(expectedSearchParam, actualSearchParam);
        }

        [Fact]
        public void GivenCaseVariantDefinitionUris_WhenGettingByUrl_ThenExactCaseUriResolvesToMatchingDefinition()
        {
            var lowerUri = "http://test.org/SearchParameter/foo-bar";
            var upperUri = "http://test.org/SearchParameter/Foo-Bar";

            var lowerDefinition = new SearchParameterInfo(
                name: "foo-bar-lower",
                code: "foo-bar",
                searchParamType: SearchParamType.Token,
                url: new Uri(lowerUri),
                expression: "Patient.identifier",
                baseResourceTypes: new List<string> { "Patient" });

            var upperDefinition = new SearchParameterInfo(
                name: "foo-bar-upper",
                code: "foo-bar",
                searchParamType: SearchParamType.Token,
                url: new Uri(upperUri),
                expression: "Patient.active",
                baseResourceTypes: new List<string> { "Patient" });

            _searchParameterDefinitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>(
                new Dictionary<string, SearchParameterInfo>
                {
                    [lowerUri] = lowerDefinition,
                    [upperUri] = upperDefinition,
                });

            SearchParameterInfo resolvedLower = _searchParameterDefinitionManager.GetSearchParameter(lowerUri);
            SearchParameterInfo resolvedUpper = _searchParameterDefinitionManager.GetSearchParameter(upperUri);

            Assert.Same(lowerDefinition, resolvedLower);
            Assert.Same(upperDefinition, resolvedUpper);

            Assert.False(_searchParameterDefinitionManager.TryGetSearchParameter("http://test.org/SearchParameter/FOO-BAR", out _));
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenGettingNonexistentSearchParameterByUrl_ExceptionIsThrown()
        {
            Assert.Throws<SearchParameterNotSupportedException>(() => _searchParameterDefinitionManager.GetSearchParameter(_testSearchParamInfo.Url.OriginalString));
        }

        [Fact]
        public async Task GivenASearchParameterDefinitionManager_WhenGettingSearchParameterHashForExistingResourceType_ThenHashIsReturned()
        {
            // Initialize a search parameter
            var searchParam = new SearchParameter()
            {
                Url = "http://test/Patient-test",
                Type = Hl7.Fhir.Model.SearchParamType.String,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient},
#endif
                Expression = "Patient.Name",
                Name = "test",
                Code = "test",
            };

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(p => p.Name == "test"))
                .Returns((true, false));

            await _searchParameterOperations.AddSearchParameterAsync(searchParam.ToTypedElement(), CancellationToken.None);

            var searchParamHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient");
            Assert.NotNull(searchParamHash);
        }

        [Fact]
        public async Task GivenASearchParameterDefinitionManager_WhenAddingACompositeSearchParameter_ThenComponentsAreResolvedAndValidated()
        {
#if Stu3
            ResourceReference CreateDefinition(string reference)
            {
                return new ResourceReference(reference);
            }
#else
            string CreateDefinition(string reference)
            {
                return reference;
            }
#endif

            // Initialize a search parameter
            var searchParam = new SearchParameter()
            {
                Url = "http://test/Patient-test",
                Type = Hl7.Fhir.Model.SearchParamType.Composite,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.Patient },
#endif
                Expression = "Patient",
                Name = "test",
                Code = "test",
                Component = new List<SearchParameter.ComponentComponent>
                {
                    new()
                    {
                        Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/Resource-id"),
                        Expression = "id",
                    },
                    new()
                    {
                        Definition = CreateDefinition("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"),
                        Expression = "meta.lastUpdated",
                    },
                },
            };

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(p => p.Name == "test" && p.Component.All(c => c.ResolvedSearchParameter != null)))
                .Returns((true, false));

            await _searchParameterOperations.AddSearchParameterAsync(searchParam.ToTypedElement(), CancellationToken.None);

            var addedParam = _searchParameterDefinitionManager.GetSearchParameter("http://test/Patient-test");
            Assert.NotNull(addedParam);
        }

        [Fact]
        public void GivenASearchParameterDefinitionManager_WhenGettingSearchParameterHashForMissingResourceType_ThenNullIsReturned()
        {
            var searchParamHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Foo");
            Assert.Null(searchParamHash);
        }

        [Fact]
        public async Task GivenASPDefinitionManager_WhenInitialed_ThenSearchParametersHashHasValues()
        {
            await _searchParameterDefinitionManager.Handle(new Messages.Search.SearchParametersUpdatedNotification(new List<SearchParameterInfo>()), CancellationToken.None);
            var searchParams = _searchParameterDefinitionManager.GetSearchParameters("Patient");
            var patientHash = searchParams.CalculateSearchParameterHash();

            Assert.Equal(patientHash, _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
        }

        [Fact]
        public async Task GivenASearchParameterDefinitionManager_WhenAddingNewParameter_ThenParameterIsAdded()
        {
            var patientParams = _searchParameterDefinitionManager.GetSearchParameters("Patient");
            var patientParamCount = patientParams.Count();

            // Initialize a search parameter
            var searchParam = new SearchParameter()
            {
                Url = "http://test/Patient-test",
                Type = Hl7.Fhir.Model.SearchParamType.String,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.Patient },
#endif
                Expression = "Patient.name",
                Name = "test",
                Code = "test",
            };

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(p => p.Name == "test"))
                .Returns((true, false));

            await _searchParameterOperations.AddSearchParameterAsync(searchParam.ToTypedElement(), CancellationToken.None);

            var patientParamsWithNew = _searchParameterDefinitionManager.GetSearchParameters("Patient");
            Assert.Equal(patientParamCount + 1, patientParamsWithNew.Count());
        }

        [Fact]
        public async Task GivenExistingSearchParameters_WhenStartingSearchParameterDefinitionManager_ThenExistingParametersAdded()
        {
            // Create some existing search paramters that will be returned when searching for resources
            // of type SearchParameter
            var searchParam = new SearchParameter()
            {
                Id = "id",
                Url = "http://test/Patient-preexisting",
                Type = Hl7.Fhir.Model.SearchParamType.String,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.Patient },
#endif
                Expression = "Patient.name",
                Name = "preexisting",
                Code = "preexisting",
            };
            SearchResult result = GetSearchResultFromSearchParam(searchParam, "token");

            var searchParam2 = new SearchParameter()
            {
                Id = "id2",
                Url = "http://test/Patient-preexisting2",
                Type = Hl7.Fhir.Model.SearchParamType.String,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.Patient },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.Patient },
#endif
                Expression = "Patient.name",
                Name = "preexisting2",
                Code = "preexisting2",
            };
            SearchResult result2 = GetSearchResultFromSearchParam(searchParam2, "token2");

            var searchParam3 = new SearchParameter()
            {
                Id = "QuestionnaireResponse-questionnaire2",
                Url = "http://hl7.org/fhir/SearchParameter/QuestionnaireResponse-questionnaire2",
                Type = Hl7.Fhir.Model.SearchParamType.Reference,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.QuestionnaireResponse },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.QuestionnaireResponse },
#endif
                Expression = "QuestionnaireResponse.questionnaire",
                Name = "questionnaire2",
                Code = "questionnaire2",
            };
            SearchResult result3 = GetSearchResultFromSearchParam(searchParam3, "token3");

            var searchParam4 = new SearchParameter()
            {
                Id = "QuestionnaireResponse-questionnaire",
                Url = "http://hl7.org/fhir/SearchParameter/QuestionnaireResponse-questionnaire",
                Type = Hl7.Fhir.Model.SearchParamType.Reference,
#if Stu3 || R4 || R4B
                Base = new List<ResourceType?>() { ResourceType.QuestionnaireResponse },
#else
                Base = new List<VersionIndependentResourceTypesAll?>() { VersionIndependentResourceTypesAll.QuestionnaireResponse },
#endif
                Expression = "QuestionnaireResponse.questionnaire",
                Name = "questionnaire",
                Code = "questionnaire",
            };
            SearchResult result4 = GetSearchResultFromSearchParam(searchParam4, null);

            var searchService = Substitute.For<ISearchService>();

            searchService.SearchAsync(Arg.Is<SearchOptions>(options => options.ContinuationToken == null), Arg.Any<CancellationToken>())
                .Returns(result);
            searchService.SearchAsync(
                Arg.Is<SearchOptions>(
                    options => options.ContinuationToken == "token"),
                Arg.Any<CancellationToken>())
                .Returns(result2);
            searchService.SearchAsync(
               Arg.Is<SearchOptions>(
                   options => options.ContinuationToken == "token2"),
               Arg.Any<CancellationToken>())
               .Returns(result3);
            searchService.SearchAsync(
               Arg.Is<SearchOptions>(
                   options => options.ContinuationToken == "token3"),
               Arg.Any<CancellationToken>())
               .Returns(result4);

            var dataStoreSearchParamValidator = Substitute.For<IDataStoreSearchParameterValidator>();
            dataStoreSearchParamValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(true);

            _searchParameterSupportResolver
                .IsSearchParameterSupported(Arg.Is<SearchParameterInfo>(s => s.Name.StartsWith("preexisting")))
                .Returns((true, false));

            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _mediator,
                searchService.CreateMockScopeProvider(),
                _searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);

            await searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);

            var statusManager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            await statusManager.EnsureInitializedAsync(CancellationToken.None);

            var patientParams = searchParameterDefinitionManager.GetSearchParameters("Patient");
            Assert.False(patientParams.Where(p => p.Name == "preexisting").First().IsSearchable);
            Assert.True(patientParams.Where(p => p.Name == "preexisting2").First().IsSearchable);

            var questionnaireParams = searchParameterDefinitionManager.GetSearchParameters("QuestionnaireResponse");
            Assert.Single(questionnaireParams, p => p.Name == "questionnaire2");
        }

        [Fact]
        public void GivenExistingSearchParameters_WhenDeletingSearchParameter_ThenSearchParameterShouldBeDeleted()
        {
            var searchParameters = new SearchParameterInfo[]
            {
                new SearchParameterInfo(
                    name: "us-core-race",
                    code: "us-core-race",
                    searchParamType: SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-race4"),
                    expression: "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3 | Patient.exp4",
                    baseResourceTypes: new List<string> { "Patient" }),
                new SearchParameterInfo(
                    name: "us-core-race",
                    code: "us-core-race",
                    searchParamType: SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-race3"),
                    expression: "Patient.exp | Patient.exp1 | Patient.exp2 | Patient.exp3",
                    baseResourceTypes: new List<string> { "Patient" }),
                new SearchParameterInfo(
                    name: "us-core-race",
                    code: "us-core-race",
                    searchParamType: SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-race2"),
                    expression: "Patient.exp | Patient.exp1 | Patient.exp2",
                    baseResourceTypes: new List<string> { "Patient" }),
                new SearchParameterInfo(
                    name: "us-core-race",
                    code: "us-core-race",
                    searchParamType: SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-race1"),
                    expression: "Patient.exp | Patient.exp1",
                    baseResourceTypes: new List<string> { "Patient" }),
                new SearchParameterInfo(
                    name: "us-core-race",
                    code: "us-core-race",
                    searchParamType: SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-race"),
                    expression: "Patient.exp",
                    baseResourceTypes: new List<string> { "Patient" }),
            };

            _searchParameterDefinitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>(
                searchParameters.ToDictionary(x => x.Url.OriginalString));
            _searchParameterDefinitionManager.TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>();
            _searchParameterDefinitionManager.TypeLookup.TryAdd(
                "Patient",
                new ConcurrentDictionary<string, ConcurrentQueue<string>>(searchParameters.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => new ConcurrentQueue<string>(x.Select(sp => sp.Url.OriginalString)))));

            for (int i = searchParameters.Length - 1; i >= 0; i--)
            {
                _searchParameterDefinitionManager.DeleteSearchParameter(searchParameters[i].Url.OriginalString);
                Assert.DoesNotContain(
                    _searchParameterDefinitionManager.UrlLookup,
                    _ => _searchParameterDefinitionManager.UrlLookup.TryGetValue(searchParameters[i].Url.OriginalString, out var _));

                if (_searchParameterDefinitionManager.TypeLookup.TryGetValue("Patient", out var patientLookup) &&
                    patientLookup.TryGetValue(searchParameters[i].Code, out var queue))
                {
                    Assert.DoesNotContain(queue, uri => string.Equals(uri, searchParameters[i].Url.OriginalString, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        [Fact]
        public void GivenTypeLookup_WhenGettingByResourceAndCode_ThenSearchParameterResolvesFromUrlLookup()
        {
            var searchParam = new SearchParameterInfo(
                name: "binding-test",
                code: "binding-test",
                searchParamType: SearchParamType.Token,
                url: new Uri("http://example.org/fhir/SearchParameter/binding-test"),
                expression: "Patient.identifier",
                baseResourceTypes: new List<string> { "Patient" });

            _searchParameterDefinitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>(
                new Dictionary<string, SearchParameterInfo>
                {
                    [searchParam.Url.OriginalString] = searchParam,
                });

            _searchParameterDefinitionManager.TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>(
                new Dictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>
                {
                    ["Patient"] = new ConcurrentDictionary<string, ConcurrentQueue<string>>(
                        new Dictionary<string, ConcurrentQueue<string>>
                        {
                            ["binding-test"] = new ConcurrentQueue<string>(new[] { searchParam.Url.OriginalString }),
                        }),
                });

            SearchParameterInfo resolved = _searchParameterDefinitionManager.GetSearchParameter("Patient", "binding-test");
            Assert.Same(searchParam, resolved);
        }

        private static void ValidateSearchParam(SearchParameterInfo expectedSearchParam, SearchParameterInfo actualSearchParam)
        {
            Assert.Equal(expectedSearchParam.Code, actualSearchParam.Code);
            Assert.Equal(expectedSearchParam.Type, actualSearchParam.Type);
            Assert.Equal(expectedSearchParam.Url, actualSearchParam.Url);
        }

        private static SearchResult GetSearchResultFromSearchParam(SearchParameter searchParam, string continuationToken)
        {
            var serializer = new FhirJsonSerializer();

            var rawResource = new RawResource(
                    serializer.SerializeToString(searchParam),
                    FhirResourceFormat.Json,
                    false);

            var wrapper = new ResourceWrapper(
                new ResourceElement(
                    rawResource.ToITypedElement(ModelInfoProvider.Instance)),
                rawResource,
                new ResourceRequest("POST"),
                false,
                null,
                null,
                null);
            var searchEntry = new SearchResultEntry(wrapper);
            return new SearchResult(Enumerable.Repeat(searchEntry, 1), continuationToken, null, new List<Tuple<string, string>>());
        }

        /// <summary>
        /// Reproduces the SearchParameterNotSupportedException bug that occurs in production
        /// when the _type parameter has no entry in the Cosmos status store and a background task
        /// (no HTTP request context) accesses SearchOptionsFactory.ResourceTypeSearchParameter.
        ///
        /// The bug chain:
        /// 1. Refactor commit (4493ae1f9) registered ResourceTypeSearchParameter in UrlLookup,
        ///    making _type visible in AllSearchParameters for the first time.
        /// 2. EnsureInitializedAsync iterates AllSearchParameters, finds no status store entry
        ///    for _type, enters the else branch, and calls CheckSearchParameterSupport.
        /// 3. SearchParameterSupportResolver.IsSearchParameterSupported throws
        ///    "No target resources defined" because _type has no BaseResourceTypes/TargetResourceTypes.
        /// 4. The exception is caught → IsSearchable is set to false.
        /// 5. Background task calls SearchableSearchParameterDefinitionManager.GetSearchParameter("Resource", "_type").
        /// 6. No HTTP request context → UsePartialSearchParams returns false.
        /// 7. IsSearchable is false AND UsePartialSearchParams is false → throws SearchParameterNotSupportedException.
        /// </summary>
        [Fact]
        public async Task GivenResourceTypeParamWithNoStatusStoreEntry_WhenBackgroundTaskAccessesSearchableManager_ThenShouldNotThrow()
        {
            // Arrange: Create a status store that has NO entry for _type (Resource-type),
            // matching the actual production condition on failing Cosmos/Gen1 accounts.
            // Only include entries for other parameters — _type is deliberately absent.
            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },

                    // Note: NO entry for _type (http://hl7.org/fhir/SearchParameter/Resource-type).
                    // This is the actual production condition — the status store simply doesn't
                    // have a row for it, so it hits the else branch in EnsureInitializedAsync.
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var searchService = Substitute.For<ISearchService>();
            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _mediator,
                searchService.CreateMockScopeProvider(),
                _searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);

            await searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);

            var statusManager = new SearchParameterStatusManager(
                statusDataStore,
                searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            await statusManager.EnsureInitializedAsync(CancellationToken.None);

            // Verify the fix: _type.IsSearchable should remain true because it is ResourceTypeSearchParameter,
            // which is a hardcoded parameter with no status store entry and cannot be
            // validated by SearchParameterSupportResolver (throws "No target resources defined").
            // Before the fix, EnsureInitializedAsync set IsSearchable = false for any param
            // not found in the status store (the else branch), which hit _type when the status
            // store had no entry for it.
            var resourceTypeParam = searchParameterDefinitionManager.GetSearchParameter("Resource", SearchParameterNames.ResourceType);
            Assert.True(resourceTypeParam.IsSearchable, "_type should remain searchable even without a status store entry");
            Assert.True(resourceTypeParam.IsSupported, "_type should remain supported even without a status store entry");

            // Simulate a background task context: RequestContext is null (no HTTP request)
            var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            var searchableManager = new SearchableSearchParameterDefinitionManager(
                searchParameterDefinitionManager, nullContextAccessor);

            // Act & Assert: This is the exact call that crashes in production.
            // SearchOptionsFactory.ResourceTypeSearchParameter calls this with ("Resource", "_type").
            // Before the fix, this throws SearchParameterNotSupportedException because:
            //   1. _type has no status store entry → else branch → IsSearchable = false
            //   2. Background task has no RequestContext → UsePartialSearchParams = false
            //   3. Both false → SearchableSearchParameterDefinitionManager throws
            var exception = Record.Exception(() =>
                searchableManager.GetSearchParameter("Resource", SearchParameterNames.ResourceType));

            Assert.Null(exception);
        }

        /// <summary>
        /// Verifies that _type (ResourceTypeSearchParameter) remains searchable and supported
        /// after multiple calls to EnsureInitializedAsync (simulating pod restarts or repeated
        /// initialization), even though _type never gets written to the status store.
        ///
        /// _type is a hardcoded parameter with no corresponding FHIR SearchParameter resource.
        /// The refresh loop in SearchParameterOperations explicitly filters out system-defined
        /// parameters before fetching, so _type is never persisted to the status store.
        /// This test ensures the fix is stable across repeated initialization cycles.
        /// </summary>
        [Fact]
        public async Task GivenResourceTypeParamWithNoStatusStoreEntry_WhenMultipleInitializationCyclesOccur_ThenRemainsSearchable()
        {
            // Arrange: Status store with NO entry for _type.
            // This matches existing Cosmos/Gen1 accounts whose status store was seeded
            // BEFORE the refactor commit added _type to AllSearchParameters.
            // New instances DO have _type in the store (seeded by CosmosDbSearchParameterStatusInitializer
            // via FilebasedSearchParameterStatusDataStore which iterates AllSearchParameters).
            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },

                    // _type is deliberately absent from the status store.
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var searchService = Substitute.For<ISearchService>();
            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _mediator,
                searchService.CreateMockScopeProvider(),
                _searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);

            await searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);

            var statusManager = new SearchParameterStatusManager(
                statusDataStore,
                searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            // Act: Run EnsureInitializedAsync multiple times, simulating repeated refresh
            // cycles where the status store still has no _type entry.
            for (int i = 0; i < 3; i++)
            {
                await statusManager.EnsureInitializedAsync(CancellationToken.None);

                var resourceTypeParam = searchParameterDefinitionManager.GetSearchParameter(
                    "Resource", SearchParameterNames.ResourceType);
                Assert.True(resourceTypeParam.IsSearchable, $"_type should remain searchable after initialization cycle {i + 1}");
                Assert.True(resourceTypeParam.IsSupported, $"_type should remain supported after initialization cycle {i + 1}");
            }

            // Also verify that ApplySearchParameterStatus (called during refresh) doesn't
            // affect _type, since it only processes statuses passed to it and _type has none.
            await statusManager.ApplySearchParameterStatus(
                new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                },
                CancellationToken.None);

            var typeParamAfterApply = searchParameterDefinitionManager.GetSearchParameter(
                "Resource", SearchParameterNames.ResourceType);
            Assert.True(typeParamAfterApply.IsSearchable, "_type should remain searchable after ApplySearchParameterStatus");
            Assert.True(typeParamAfterApply.IsSupported, "_type should remain supported after ApplySearchParameterStatus");
        }

        /// <summary>
        /// Validates the new instance path: when the status store DOES contain an Enabled entry
        /// for _type (as seeded by CosmosDbSearchParameterStatusInitializer on new instances),
        /// _type should remain searchable and supported through initialization.
        /// </summary>
        [Fact]
        public async Task GivenResourceTypeParamWithEnabledStatusStoreEntry_WhenInitializing_ThenRemainsSearchable()
        {
            // Arrange: Status store WITH an Enabled entry for _type, matching new instances
            // where CosmosDbSearchParameterStatusInitializer seeds all AllSearchParameters.
            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            statusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = SearchParameterNames.ResourceTypeUri,
                    },
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var searchService = Substitute.For<ISearchService>();
            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                _mediator,
                searchService.CreateMockScopeProvider(),
                _searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);

            await searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);

            var statusManager = new SearchParameterStatusManager(
                statusDataStore,
                searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator,
                NullLogger<SearchParameterStatusManager>.Instance);

            await statusManager.EnsureInitializedAsync(CancellationToken.None);

            // Verify: _type should be searchable via the normal if-branch (status found, Enabled).
            var resourceTypeParam = searchParameterDefinitionManager.GetSearchParameter(
                "Resource", SearchParameterNames.ResourceType);
            Assert.True(resourceTypeParam.IsSearchable, "_type should be searchable when status store has Enabled entry");
            Assert.True(resourceTypeParam.IsSupported, "_type should be supported when status store has Enabled entry");

            // Also verify it works from SearchableSearchParameterDefinitionManager in
            // a background task context (null RequestContext).
            var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            var searchableManager = new SearchableSearchParameterDefinitionManager(
                searchParameterDefinitionManager, nullContextAccessor);

            var exception = Record.Exception(() =>
                searchableManager.GetSearchParameter("Resource", SearchParameterNames.ResourceType));

            Assert.Null(exception);
        }
    }
}
