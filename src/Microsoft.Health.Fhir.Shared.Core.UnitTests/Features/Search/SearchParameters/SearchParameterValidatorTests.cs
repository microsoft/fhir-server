// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterValidatorTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IAuthorizationService<DataActions> _authorizationService = new DisabledFhirAuthorizationService();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IModelInfoProvider _modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes("Patient").Build();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer = Substitute.For<ISearchParameterComparer<SearchParameterInfo>>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly IScoped<IFhirDataStore> _fhirDataStoreScoped;

        public SearchParameterValidatorTests()
        {
            _fhirDataStoreScoped = _fhirDataStore.CreateMockScope();

            SearchParameterInfo searchParameterInfo = new SearchParameterInfo(
                "USCoreRace",
                "race",
                ValueSets.SearchParamType.String,
                new System.Uri("http://duplicate"))
            {
                SearchParameterStatus = SearchParameterStatus.Supported,
            };

            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Is<string>(uri => uri != "http://duplicate"), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("http://duplicate", out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = searchParameterInfo;
                    return true;
                });

            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", Arg.Is<string>(code => code != "duplicate"), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", "duplicate", out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = searchParameterInfo;
                    return true;
                });

            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Is<string>(uri => uri != "http://duplicate"), Arg.Any<bool>(), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("http://duplicate", Arg.Any<bool>(), out _).Returns(true);
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", Arg.Is<string>(code => code != "duplicate"), Arg.Any<bool>(), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", "duplicate", Arg.Any<bool>(), out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[3] = searchParameterInfo;
                    return true;
                });
            _fhirOperationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None).Returns((false, string.Empty));

            // Setup GetAsync to return a resource for "duplicate" (custom parameter)
            // Extract ID from URL: "http://duplicate" -> "duplicate"
            var mockResourceWrapper = Substitute.For<ResourceWrapper>();
            _fhirDataStore.GetAsync(
                Arg.Is<ResourceKey>(key => key.Id == "duplicate"),
                Arg.Any<CancellationToken>()).Returns(mockResourceWrapper);
        }

        [Theory]
        [MemberData(nameof(InvalidSearchParamData))]
        public async Task GivenInvalidSearchParam_WhenValidatingSearchParam_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider, _searchParameterOperations, _searchParameterComparer, _fhirDataStoreScoped, NullLogger<SearchParameterValidator>.Instance);
            await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(ValidSearchParamData))]
        public async Task GivenValidSearchParam_WhenValidatingSearchParam_ThenNoExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider, _searchParameterOperations, _searchParameterComparer, _fhirDataStoreScoped, NullLogger<SearchParameterValidator>.Instance);
            await validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None);
        }

        [Fact]
        public async Task GivenUnauthorizedUser_WhenValidatingSearchParam_ThenExceptionThrown()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.Write);
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), authorizationService, _searchParameterDefinitionManager, _modelInfoProvider, _searchParameterOperations, _searchParameterComparer, _fhirDataStoreScoped, NullLogger<SearchParameterValidator>.Instance);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => validator.ValidateSearchParameterInput(new SearchParameter(), "POST", CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(DuplicateCodeAtBaseResourceData))]
        public async Task GivenInvalidSearchParamWithDuplicateCode_WhenValidatingSearchParam_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider, _searchParameterOperations, _searchParameterComparer, _fhirDataStoreScoped, NullLogger<SearchParameterValidator>.Instance);
            await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(DuplicateUrlData))]
        public async Task GivenValidSearchParamWithDuplicateUrl_WhenValidatingSearchParamByStatus_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method, SearchParameterStatus searchParameterStatus)
        {
            _searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Url, out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = new SearchParameterInfo("USCoreRace", "race")
                    {
                        SearchParameterStatus = searchParameterStatus,
                    };

                    return true;
                });

            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider, _searchParameterOperations, _searchParameterComparer, _fhirDataStoreScoped, NullLogger<SearchParameterValidator>.Instance);
            if (searchParameterStatus == SearchParameterStatus.PendingDelete)
            {
                // Expecting no exception being thrown.
                await validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None);
            }
            else
            {
                await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None));
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(-1, 0)]
        [InlineData(int.MinValue, 0)]
        [InlineData(0, int.MinValue)]
        public async Task GivenSearchParameter_WhenValidatingProperties_ThenConflictingPropertiesShouldBeReported(int compareExpressionResult, int compareComponentResult)
        {
            var searchParameter = new SearchParameter
            {
#if Stu3 || R4 || R4B
                Base = new[] { ResourceType.DocumentReference as ResourceType? },
#else
                Base = new[] { VersionIndependentResourceTypesAll.DocumentReference as VersionIndependentResourceTypesAll? },
#endif
                Url = "http://hl7.org/fhir/SearchParameter/DocumentReference-relationship",
                Code = "relationship",
                Type = SearchParamType.Composite,
            };

            var searchParameterInfo = new SearchParameterInfo(
                name: "relationship",
                code: "relationship",
                searchParamType: ValueSets.SearchParamType.Composite,
                url: new System.Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                baseResourceTypes: new List<string> { "DocumentReference" });

            var validator = new SearchParameterValidator(
                () => _fhirOperationDataStore.CreateMockScope(),
                _authorizationService,
                _searchParameterDefinitionManager,
                _modelInfoProvider,
                _searchParameterOperations,
                _searchParameterComparer,
                _fhirDataStoreScoped,
                NullLogger<SearchParameterValidator>.Instance);

            _searchParameterDefinitionManager.TryGetSearchParameter("DocumentReference", "relationship", Arg.Any<bool>(), out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[3] = searchParameterInfo;
                    return true;
                });

            _searchParameterComparer.CompareExpression(Arg.Any<string>(), Arg.Any<string>()).Returns(compareExpressionResult);
            _searchParameterComparer.CompareComponent(Arg.Any<IEnumerable<(string, string)>>(), Arg.Any<IEnumerable<(string, string)>>()).Returns(compareComponentResult);

            try
            {
                await validator.ValidateSearchParameterInput(searchParameter, "POST", CancellationToken.None);
                Assert.True(compareExpressionResult > int.MinValue && compareComponentResult > int.MinValue);
            }
            catch (ResourceNotValidException)
            {
                Assert.False(compareExpressionResult > int.MinValue && compareComponentResult > int.MinValue);
            }
        }

        public static IEnumerable<object[]> InvalidSearchParamData()
        {
            var missingUrl = new SearchParameter();
            var duplicateUrl = new SearchParameter { Url = "http://duplicate" };
            var brokenUrl = new SearchParameter { Url = "BrokenUrl" };
            var uniqueUrl = new SearchParameter { Url = "http://unique" };
#if Stu3 || R4 || R4B
            var baseArray = new[] { ResourceType.Patient as ResourceType? };
#else
            var baseArray = new[] { VersionIndependentResourceTypesAll.Patient as VersionIndependentResourceTypesAll? };
#endif
            var duplicateCode = new SearchParameter { Url = "http://unique", Code = "duplicate", Base = baseArray };
            var nullCode = new SearchParameter { Url = "http://unique", Code = null, Base = baseArray };

            var data = new List<object[]>();
            data.Add(new object[] { duplicateCode, "PUT" });
            data.Add(new object[] { missingUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "POST" });
            data.Add(new object[] { brokenUrl, "POST" });
            data.Add(new object[] { uniqueUrl, "DELETE" });
            data.Add(new object[] { duplicateCode, "POST" });
            data.Add(new object[] { nullCode, "POST" });
            data.Add(new object[] { nullCode, "PUT" });

            // data.Add(new object[] { uniqueUrl, "PUT" }); //No exception thrown, since it should work as upsert.

            return data;
        }

        public static IEnumerable<object[]> DuplicateCodeAtBaseResourceData()
        {
#if Stu3 || R4 || R4B
            var duplicateCode1 = new SearchParameter { Url = "http://unique2", Code = "duplicate", Base = new[] { ResourceType.Resource as ResourceType? } };
#else
            var duplicateCode1 = new SearchParameter { Url = "http://unique2", Code = "duplicate", Base = new[] { VersionIndependentResourceTypesAll.Resource as VersionIndependentResourceTypesAll? } };
#endif

            var data = new List<object[]>();
            data.Add(new object[] { duplicateCode1, "POST" });
            data.Add(new object[] { duplicateCode1, "PUT" });
            return data;
        }

        public static IEnumerable<object[]> ValidSearchParamData()
        {
            var duplicateUrl = new SearchParameter { Url = "http://duplicate" };
            var uniqueUrl = new SearchParameter { Url = "http://unique" };
#if Stu3 || R4 || R4B
            var baseArray = new[] { ResourceType.Patient as ResourceType? };
#else
            var baseArray = new[] { VersionIndependentResourceTypesAll.Patient as VersionIndependentResourceTypesAll? };
#endif
            var uniqueCode = new SearchParameter { Url = "http://unique", Code = "unique", Base = baseArray };

            var data = new List<object[]>();
            data.Add(new object[] { uniqueUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "PUT" });
            data.Add(new object[] { duplicateUrl, "DELETE" });
            data.Add(new object[] { uniqueCode, "POST" });

            return data;
        }

        public static IEnumerable<object[]> DuplicateUrlData()
        {
            var searchParam = new SearchParameter { Url = "http://unique3" };

            var data = new List<object[]>();
            data.Add(new object[] { searchParam, "POST", SearchParameterStatus.Supported });
            data.Add(new object[] { searchParam, "POST", SearchParameterStatus.PendingDelete });
            return data;
        }

        [Fact]
        public async Task GivenPutRequestOnSpecDefinedSearchParameter_WhenValidatingSearchParam_ThenMethodNotAllowedExceptionThrown()
        {
            // Spec-defined search parameters exist in the definition manager but have NO stored resource
            var specDefinedUrl = "http://spec-defined";
            var searchParam = new SearchParameter { Url = specDefinedUrl, Id = "spec-123" };

            // Setup: Parameter exists in definition manager
            SearchParameterInfo specDefinedInfo = new SearchParameterInfo(
                "SpecDefined",
                "spec-code",
                ValueSets.SearchParamType.String,
                new System.Uri(specDefinedUrl))
            {
                SearchParameterStatus = SearchParameterStatus.Supported,
            };
            _searchParameterDefinitionManager.TryGetSearchParameter(specDefinedUrl, out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = specDefinedInfo;
                    return true;
                });

            // Setup: No stored resource (returns null)
            var resourceKey = new ResourceKey(KnownResourceTypes.SearchParameter, searchParam.Id);
            _fhirDataStore.GetAsync(resourceKey, Arg.Any<CancellationToken>()).Returns((ResourceWrapper)null);

            var validator = new SearchParameterValidator(
                () => _fhirOperationDataStore.CreateMockScope(),
                _authorizationService,
                _searchParameterDefinitionManager,
                _modelInfoProvider,
                _searchParameterOperations,
                _searchParameterComparer,
                _fhirDataStoreScoped,
                NullLogger<SearchParameterValidator>.Instance);

            // PUT on spec-defined SearchParameter should fail
            await Assert.ThrowsAsync<MethodNotAllowedException>(() => validator.ValidateSearchParameterInput(searchParam, "PUT", CancellationToken.None));
        }

        [Fact]
        public async Task GivenPutRequestOnCustomSearchParameter_WhenValidatingSearchParam_ThenNoExceptionThrown()
        {
            // Custom search parameters exist in BOTH the definition manager AND have a stored resource
            var customUrl = "http://custom";
            var searchParam = new SearchParameter { Url = customUrl, Id = "custom-123", Code = "custom-code" };
#if Stu3 || R4 || R4B
            searchParam.Base = new[] { ResourceType.Patient as ResourceType? };
#else
            searchParam.Base = new[] { VersionIndependentResourceTypesAll.Patient as VersionIndependentResourceTypesAll? };
#endif

            // Setup: Parameter exists in definition manager
            SearchParameterInfo customInfo = new SearchParameterInfo(
                "CustomParam",
                "custom-code",
                ValueSets.SearchParamType.String,
                new System.Uri(customUrl))
            {
                SearchParameterStatus = SearchParameterStatus.Supported,
            };
            _searchParameterDefinitionManager.TryGetSearchParameter(customUrl, out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = customInfo;
                    return true;
                });

            // Setup: Code does not conflict
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", "custom-code", Arg.Any<bool>(), out _).Returns(false);

            // Setup: Stored resource exists (does NOT throw ResourceNotFoundException)
            var resourceKey = new ResourceKey(KnownResourceTypes.SearchParameter, searchParam.Id);
            var mockResourceWrapper = Substitute.For<ResourceWrapper>();
            _fhirDataStore.GetAsync(resourceKey, Arg.Any<CancellationToken>()).Returns(mockResourceWrapper);

            var validator = new SearchParameterValidator(
                () => _fhirOperationDataStore.CreateMockScope(),
                _authorizationService,
                _searchParameterDefinitionManager,
                _modelInfoProvider,
                _searchParameterOperations,
                _searchParameterComparer,
                _fhirDataStoreScoped,
                NullLogger<SearchParameterValidator>.Instance);

            // PUT on custom SearchParameter should succeed (no exception)
            await validator.ValidateSearchParameterInput(searchParam, "PUT", CancellationToken.None);
        }

        [Fact]
        public async Task GivenDeleteRequestOnCustomSearchParameter_WhenValidatingSearchParam_ThenNoExceptionThrown()
        {
            // Custom search parameters exist in BOTH the definition manager AND have a stored resource
            var customUrl = "http://custom-delete";
            var searchParam = new SearchParameter { Url = customUrl, Id = "custom-delete-123" };

            // Setup: Parameter exists in definition manager
            SearchParameterInfo customInfo = new SearchParameterInfo(
                "CustomParamDelete",
                "custom-delete-code",
                ValueSets.SearchParamType.String,
                new System.Uri(customUrl))
            {
                SearchParameterStatus = SearchParameterStatus.Supported,
            };
            _searchParameterDefinitionManager.TryGetSearchParameter(customUrl, out Arg.Any<SearchParameterInfo>()).Returns(
                x =>
                {
                    x[1] = customInfo;
                    return true;
                });

            // Setup: Stored resource exists (does NOT throw ResourceNotFoundException)
            var resourceKey = new ResourceKey(KnownResourceTypes.SearchParameter, searchParam.Id);
            var mockResourceWrapper = Substitute.For<ResourceWrapper>();
            _fhirDataStore.GetAsync(resourceKey, Arg.Any<CancellationToken>()).Returns(mockResourceWrapper);

            var validator = new SearchParameterValidator(
                () => _fhirOperationDataStore.CreateMockScope(),
                _authorizationService,
                _searchParameterDefinitionManager,
                _modelInfoProvider,
                _searchParameterOperations,
                _searchParameterComparer,
                _fhirDataStoreScoped,
                NullLogger<SearchParameterValidator>.Instance);

            // DELETE on custom SearchParameter should succeed (no exception)
            await validator.ValidateSearchParameterInput(searchParam, "DELETE", CancellationToken.None);
        }
    }
}
