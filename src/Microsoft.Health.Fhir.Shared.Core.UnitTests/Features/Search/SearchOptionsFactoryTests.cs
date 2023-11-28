// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    /// <summary>
    /// Test class for SearchOptionsFactory.Create
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public partial class SearchOptionsFactoryTests
    {
        private const string DefaultResourceType = "Patient";
        private const string ContinuationTokenParamName = "ct";
        private const string SPURI = "http://hl7.org/fhir/SearchParameter/Patient-address-city";
        private const string SPTYPEURI = "http://hl7.org/fhir/SearchParameter/Resource-Type";
        private const string ACCOUNTURI = "http://hl7.org/fhir/SearchParameter/Account-name";

        private readonly IExpressionParser _expressionParser = Substitute.For<IExpressionParser>();
        private readonly SearchOptionsFactory _factory;
        private readonly SearchParameterInfo _resourceTypeSearchParameterInfo;
        private readonly SearchParameterInfo _lastUpdatedSearchParameterInfo;
        private readonly SearchParameterInfo _patientAddressSearchParameterInfo;
        private readonly SearchParameterInfo _accountNameSearchParameterInfo;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private DefaultFhirRequestContext _defaultFhirRequestContext;
        private readonly ISortingValidator _sortingValidator;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SearchOptionsFactoryTests()
        {
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _resourceTypeSearchParameterInfo = new SearchParameter { Name = SearchParameterNames.ResourceType, Code = SearchParameterNames.ResourceType, Type = SearchParamType.String, Url = SearchParameterNames.ResourceTypeUri.AbsoluteUri }.ToInfo();
            _lastUpdatedSearchParameterInfo = new SearchParameter { Name = SearchParameterNames.LastUpdated, Code = SearchParameterNames.LastUpdated, Type = SearchParamType.String }.ToInfo();
            _patientAddressSearchParameterInfo = new SearchParameter { Name = "address-city", Code = "address-city", Type = SearchParamType.String, Url = SPURI }.ToInfo();
            _accountNameSearchParameterInfo = new SearchParameter { Name = "name", Code = "name", Type = SearchParamType.String, Url = ACCOUNTURI }.ToInfo();
            searchParameterDefinitionManager.GetSearchParameter("Patient", "address-city").Returns(_patientAddressSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter("Patient", "address-state").Returns(_patientAddressSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter("Account", "name").Returns(_accountNameSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.ResourceType).Returns(_resourceTypeSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.LastUpdated).Returns(_lastUpdatedSearchParameterInfo);

            // searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>()).Throws(ci => new SearchParameterNotSupportedException(ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
            _searchParameterStatusManager = new SearchParameterStatusManager(
                               Substitute.For<ISearchParameterStatusDataStore>(),
                               searchParameterDefinitionManager,
                               Substitute.For<ISearchParameterSupportResolver>(),
                               Substitute.For<IMediator>(),
                               NullLogger<SearchParameterStatusManager>.Instance);
            _searchParameterStatusManager.GetAllSearchParameterStatus(Arg.Any<CancellationToken>()).Returns(new List<ResourceSearchParameterStatus>
            {
                new ResourceSearchParameterStatus()
                {
                    Uri = new Uri(SPURI),
                    Status = SearchParameterStatus.Enabled,
                },
                new ResourceSearchParameterStatus()
                {
                    Uri = new Uri(SPTYPEURI),
                    Status = SearchParameterStatus.Enabled,
                },
            });
            _coreFeatures = new CoreFeatureConfiguration();
            _defaultFhirRequestContext = new DefaultFhirRequestContext();

            _sortingValidator = Substitute.For<ISortingValidator>();

            RequestContextAccessor<IFhirRequestContext> contextAccessor = _defaultFhirRequestContext.SetupAccessor();
            _factory = new SearchOptionsFactory(
                _expressionParser,
                () => searchParameterDefinitionManager,
                new OptionsWrapper<CoreFeatureConfiguration>(_coreFeatures),
                contextAccessor,
                _sortingValidator,
                new ExpressionAccessControl(contextAccessor),
                NullLogger<SearchOptionsFactory>.Instance,
                _searchParameterStatusManager);
        }

        [Fact]
        public async void GivenANullQueryParameters_WhenCreated_ThenDefaultSearchOptionsShouldBeCreated()
        {
            SearchOptions options = await CreateSearchOptions(queryParameters: null);

            Assert.NotNull(options);

            Assert.Null(options.ContinuationToken);
            Assert.Equal(_coreFeatures.DefaultItemCountPerSearch, options.MaxItemCount);
            ValidateResourceTypeSearchParameterExpression(options.Expression, DefaultResourceType);
        }

        [Fact]
        public async void GivenMultipleContinuationTokens_WhenCreated_ThenExceptionShouldBeThrown()
        {
            const string encodedContinuationToken = "MTIz";

            await Assert.ThrowsAsync<InvalidSearchOperationException>(async () => await CreateSearchOptions(
                queryParameters: new[]
                {
                    Tuple.Create(ContinuationTokenParamName, encodedContinuationToken),
                    Tuple.Create(ContinuationTokenParamName, encodedContinuationToken),
                }));
        }

        [Fact]
        public async void GivenACount_WhenCreated_ThenCorrectMaxItemCountShouldBeSet()
        {
            SearchOptions options = await CreateSearchOptions(
                queryParameters: new[]
                {
                    Tuple.Create("_count", "5"),
                });

            Assert.NotNull(options);
            Assert.Equal(5, options.MaxItemCount);
        }

        [Fact]
        public async void GivenACountWithValueZero_WhenCreated_ThenCorrectMaxItemCountShouldBeSet()
        {
            const ResourceType resourceType = ResourceType.Encounter;
            var queryParameters = new[]
            {
               Tuple.Create("_count", "0"),
            };

            SearchOptions options = await CreateSearchOptions(
            resourceType: resourceType.ToString(),
            queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.True(options.CountOnly);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1.1")]
        public async void GivenACountWithInvalidValue_WhenCreated_ThenExceptionShouldBeThrown(string value)
        {
            const ResourceType resourceType = ResourceType.Encounter;
            var queryParameters = new[]
            {
               Tuple.Create("_count", value),
            };

            await Assert.ThrowsAsync<System.FormatException>(async () => await CreateSearchOptions(
            resourceType: resourceType.ToString(),
            queryParameters: queryParameters));
        }

        [Fact]
        public async void GivenNoneOfTheSearchParamIsSupported_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName1 = "address-city";
            const string value1 = "Seattle";

            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName1, value1).Returns(
                x => throw new SearchParameterNotSupportedException(typeof(Patient), paramName1));

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            ValidateResourceTypeSearchParameterExpression(options.Expression, resourceType.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public async void GivenASearchParamWithEmptyValue_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedList(string value)
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName = "address-city";

            var queryParameters = new[]
            {
                Tuple.Create(paramName, value),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.Equal(queryParameters, options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenASearchParameterWithEmptyKey_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(string.Empty, "city"),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters: queryParameters);
            Assert.NotNull(options);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenSearchParametersWithEmptyKey_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create("address-city", "Oklahoma"),
                Tuple.Create(string.Empty, "anotherCity"),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.Single(options.UnsupportedSearchParams);
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenSearchParametersWithEmptyKeyEmptyValue_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(" ", "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Equal(2, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams.Take(1));
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams.Skip(1).Take(1));
        }

        [Fact]
        public async void GivenSearchParametersWithEmptyKeyEmptyValueWithAnotherValidParameter_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create("address-city", "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Single(options.UnsupportedSearchParams);
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenSearchParametersWithEmptyKeyEmptyValueWithAnotherInvalidParameter_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(string.Empty, "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Equal(2, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams.Take(1));
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams.Skip(1).Take(1));
        }

        [Fact]
        public async void GivenASearchParamWithInvalidValue_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedList()
        {
            const string paramName1 = "_count";
            const string value1 = "";
            const string paramName2 = "address-city";
            const string value2 = "Seattle";

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenSearchWithUnsupportedSortValue_WhenCreated_ThenSortingShouldBeEmptyAndOperationOutcomeIssueCreated()
        {
            const string paramName = SearchParameterNames.ResourceType;

            const string errorMessage = "my error";

            _sortingValidator.ValidateSorting(default, out Arg.Any<IReadOnlyList<string>>()).ReturnsForAnyArgs(x =>
            {
                x[1] = new[] { errorMessage };
                return false;
            });

            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Sort, paramName),
                Tuple.Create(KnownQueryParameterNames.Sort, "-" + paramName),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Sort);
            Assert.Empty(options.Sort);

            Assert.Contains(_defaultFhirRequestContext.BundleIssues, issue => issue.Diagnostics == errorMessage);
        }

        [Theory]
        [InlineData(SearchParameterNames.LastUpdated, SortOrder.Ascending)]
        [InlineData("-" + SearchParameterNames.LastUpdated, SortOrder.Descending)]
        public async void GivenSearchWithSupportedSortValue_WhenCreated_ThenSearchParamShouldBeAddedToSortList(string paramName, SortOrder sortOrder)
        {
            _sortingValidator.ValidateSorting(default, out var errors).ReturnsForAnyArgs(true);

            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Sort, paramName),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Sort);
            Assert.Equal((_lastUpdatedSearchParameterInfo, sortOrder), Assert.Single(options.Sort));
        }

        [Fact]
        public async void GivenSearchWithAnInvalidSortValue_WhenCreated_ThenAnOperationOutcomeIssueIsCreated()
        {
            const string paramName = "unknownParameter";

            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Sort, paramName),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Sort);
            Assert.Empty(options.Sort);

            Assert.Contains(_defaultFhirRequestContext.BundleIssues, issue => issue.Code == OperationOutcomeConstants.IssueType.NotSupported);
        }

        [Theory]
        [Trait(Traits.Category, Categories.CompartmentSearch)]
        [InlineData(ResourceType.Patient, CompartmentType.Patient, "123")]
        [InlineData(ResourceType.Appointment, CompartmentType.Device, "abc")]
        [InlineData(ResourceType.Patient, CompartmentType.Encounter, "aaa")]
        [InlineData(ResourceType.Condition, CompartmentType.Practitioner, "9aa")]
        [InlineData(ResourceType.Patient, CompartmentType.RelatedPerson, "fdsfasfasfdas")]
        [InlineData(ResourceType.Claim, CompartmentType.Encounter, "ksd;/fkds;kfsd;kf")]
        public async void GivenAValidCompartmentSearch_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(ResourceType resourceType, CompartmentType compartmentType, string compartmentId)
        {
            SearchOptions options = await CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: null,
                compartmentType: compartmentType.ToString(),
                compartmentId: compartmentId);

            Assert.NotNull(options);
            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => ValidateCompartmentSearchExpression(e, compartmentType.ToString(), compartmentId));
        }

        [Theory]
        [Trait(Traits.Category, Categories.CompartmentSearch)]
        [InlineData(CompartmentType.Patient, "123")]
        [InlineData(CompartmentType.Device, "abc")]
        [InlineData(CompartmentType.Encounter, "aaa")]
        [InlineData(CompartmentType.Practitioner, "9aa")]
        [InlineData(CompartmentType.RelatedPerson, "fdsfasfasfdas")]
        [InlineData(CompartmentType.Encounter, "ksd;/fkds;kfsd;kf")]
        public async void GivenAValidCompartmentSearchWithNullResourceType_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(CompartmentType compartmentType, string compartmentId)
        {
            SearchOptions options = await CreateSearchOptions(
                resourceType: null,
                queryParameters: null,
                compartmentType: compartmentType.ToString(),
                compartmentId: compartmentId);

            Assert.NotNull(options);
            ValidateCompartmentSearchExpression(options.Expression, compartmentType.ToString(), compartmentId);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("12223a2424")]
        [InlineData("fsdfsdf")]
        [InlineData("patients")]
        [InlineData("encounter")]
        [InlineData("Devices")]
        public async void GivenInvalidCompartmentType_WhenCreated_ThenExceptionShouldBeThrown(string invalidCompartmentType)
        {
            InvalidSearchOperationException exception = await Assert.ThrowsAsync<InvalidSearchOperationException>(async () => await CreateSearchOptions(
                resourceType: null,
                queryParameters: null,
                compartmentType: invalidCompartmentType,
                compartmentId: "123"));

            Assert.Equal(exception.Message, $"Compartment type {invalidCompartmentType} is invalid.");
        }

        [Theory]
        [InlineData("    ")]
        [InlineData("")]
        [InlineData("       ")]
        [InlineData("\t\t")]
        public async void GivenInvalidCompartmentId_WhenCreated_ThenExceptionShouldBeThrown(string invalidCompartmentId)
        {
            InvalidSearchOperationException exception = await Assert.ThrowsAsync<InvalidSearchOperationException>(async () => await CreateSearchOptions(
                resourceType: ResourceType.Claim.ToString(),
                queryParameters: null,
                compartmentType: CompartmentType.Patient.ToString(),
                compartmentId: invalidCompartmentId));

            Assert.Equal("Compartment id is null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(TotalType.Accurate)]
        [InlineData(TotalType.None)]
        public async void GivenNoTotalParameter_WhenCreated_ThenDefaultSearchOptionsShouldHaveCountWhenConfiguredByDefault(TotalType type)
        {
            _coreFeatures.IncludeTotalInBundle = type;

            SearchOptions options = await CreateSearchOptions(queryParameters: null);

            Assert.Equal(type, options.IncludeTotal);
        }

        [Fact]
        public async void GivenTotalParameter_WhenCreated_ThenDefaultSearchOptionsShouldOverrideDefault()
        {
            _coreFeatures.IncludeTotalInBundle = TotalType.Accurate;

            SearchOptions options = await CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_total", "none"), });

            Assert.Equal(TotalType.None, options.IncludeTotal);
        }

        [Fact]
        public async void GivenNoTotalParameterWithInvalidDefault_WhenCreated_ThenDefaultSearchOptionsThrowException()
        {
            _coreFeatures.IncludeTotalInBundle = TotalType.Estimate;

            await Assert.ThrowsAsync<SearchOperationNotSupportedException>(async () => await CreateSearchOptions(queryParameters: null));
        }

        [Fact]
        public async void GivenNoCountParameter_WhenCreated_ThenDefaultSearchOptionShouldUseConfigurationValue()
        {
            _coreFeatures.MaxItemCountPerSearch = 10;
            _coreFeatures.DefaultItemCountPerSearch = 3;

            SearchOptions options = await CreateSearchOptions();
            Assert.Equal(3, options.MaxItemCount);
        }

        [Fact]
        public async void GivenCountParameterBelowThanMaximumAllowed_WhenCreated_ThenDefaultSearchOptionShouldBeCreatedAndCountParameterShouldBeUsed()
        {
            _coreFeatures.MaxItemCountPerSearch = 20;
            _coreFeatures.DefaultItemCountPerSearch = 1;

            SearchOptions options = await CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_count", "10"), });
            Assert.Equal(10, options.MaxItemCount);
        }

        [Fact]
        public async void GivenCountParameterAboveThanMaximumAllowed_WhenCreated_ThenSearchOptionsAddIssueToContext()
        {
            _coreFeatures.MaxItemCountPerSearch = 10;
            _coreFeatures.DefaultItemCountPerSearch = 1;

            await CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_count", "11"), });

            Assert.Collection(_defaultFhirRequestContext.BundleIssues, issue => issue.Diagnostics.Contains("exceeds limit"));
        }

        [Fact]
        public async void GivenSetCoreFeatureForIncludeCount_WhenCreated_ThenSearchOptionsHaveSameValue()
        {
            _coreFeatures.DefaultIncludeCountPerSearch = 9;

            SearchOptions options = await CreateSearchOptions();
            Assert.Equal(_coreFeatures.DefaultIncludeCountPerSearch, options.IncludeCount);
        }

        [Fact]
        public async void GivenSearchParameterText_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Text, "mobile"),
            };

            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.Single(options.UnsupportedSearchParams);
        }

        [Fact]
        public async void GivenASearchParamThatIsNotInEnabledState_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedList()
        {
            const ResourceType resourceType = ResourceType.Account;
            const string paramName = "name";
            var queryParameters = new[]
            {
                Tuple.Create(paramName, ACCOUNTURI),
            };
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameter(resourceType.ToString(), paramName).Returns(_accountNameSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.ResourceType).Returns(_resourceTypeSearchParameterInfo);
            RequestContextAccessor<IFhirRequestContext> contextAccessor = _defaultFhirRequestContext.SetupAccessor();
            var referenceParser = Substitute.For<IReferenceSearchValueParser>();
            var searchParameterParser = new SearchParameterExpressionParser(referenceParser);
            var expressionParser = new ExpressionParser(() => searchParameterDefinitionManager, searchParameterParser);
            var spDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            spDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>()).Returns(
                new List<ResourceSearchParameterStatus>
                {
                    new ResourceSearchParameterStatus()
                    {
                        Status = SearchParameterStatus.Disabled,
                        Uri = new Uri(ACCOUNTURI),
                    },
                });
            var spStatusManager = new SearchParameterStatusManager(spDataStore, searchParameterDefinitionManager, Substitute.For<ISearchParameterSupportResolver>(), Substitute.For<IMediator>(), NullLogger<SearchParameterStatusManager>.Instance);
            var factory = new SearchOptionsFactory(
                expressionParser,
                () => searchParameterDefinitionManager,
                new OptionsWrapper<CoreFeatureConfiguration>(_coreFeatures),
                contextAccessor,
                _sortingValidator,
                new ExpressionAccessControl(contextAccessor),
                NullLogger<SearchOptionsFactory>.Instance,
                spStatusManager);

            SearchOptions options = await factory.Create(resourceType.ToString(), queryParameters, cancellationToken: default);
            Assert.NotNull(options);
            Assert.Equal(queryParameters, options.UnsupportedSearchParams);
        }

        [Theory]
        [InlineData(ResourceVersionType.Latest)]
        [InlineData(ResourceVersionType.Histoy)]
        [InlineData(ResourceVersionType.SoftDeleted)]
        [InlineData(ResourceVersionType.Latest | ResourceVersionType.Histoy)]
        [InlineData(ResourceVersionType.Latest | ResourceVersionType.SoftDeleted)]
        [InlineData(ResourceVersionType.Histoy | ResourceVersionType.SoftDeleted)]
        [InlineData(ResourceVersionType.Latest | ResourceVersionType.Histoy | ResourceVersionType.SoftDeleted)]
        public async void GivenIncludeHistoryAndDeletedParameters_WhenCreated_ThenSearchParametersShouldMatchInput(ResourceVersionType resourceVersionTypes)
        {
            SearchOptions options = await CreateSearchOptions(ResourceType.Patient.ToString(), new List<Tuple<string, string>>(), resourceVersionTypes);
            Assert.NotNull(options);
            Assert.Equal(resourceVersionTypes, options.ResourceVersionTypes);
            Assert.Empty(options.UnsupportedSearchParams);
        }

        private async Task<SearchOptions> CreateSearchOptions(
            string resourceType = DefaultResourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters = null,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            string compartmentType = null,
            string compartmentId = null,
            CancellationToken cancellationToken = default)
        {
            return await _factory.Create(compartmentType, compartmentId, resourceType, queryParameters, resourceVersionTypes: resourceVersionTypes, cancellationToken: cancellationToken);
        }
    }
}
