// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
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

        private readonly ISearchParameterStatusManager _manager;
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
        private ISearchService _searchService = Substitute.For<ISearchService>();

        public SearchParameterDefinitionManagerTests()
        {
            _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            _mediator = Substitute.For<IMediator>();
            _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
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

        public async Task InitializeAsync()
        {
            await _searchParameterDefinitionManager.EnsureInitializedAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

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
                Base = new List<ResourceType?>() { ResourceType.Patient },
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
                Base = new List<ResourceType?>() { ResourceType.Patient },
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
                Base = new List<ResourceType?>() { ResourceType.Patient },
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
                Base = new List<ResourceType?>() { ResourceType.Patient },
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
                Base = new List<ResourceType?>() { ResourceType.Patient },
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
                Base = new List<ResourceType?>() { ResourceType.QuestionnaireResponse },
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
                Base = new List<ResourceType?>() { ResourceType.QuestionnaireResponse },
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

            var searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, _mediator, () => searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);

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
            Assert.Single(questionnaireParams.Where(p => p.Name == "questionnaire2"));
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
    }
}
