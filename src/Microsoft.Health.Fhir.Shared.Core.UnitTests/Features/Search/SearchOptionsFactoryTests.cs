// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
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

        private readonly IExpressionParser _expressionParser = Substitute.For<IExpressionParser>();
        private readonly SearchOptionsFactory _factory;
        private readonly SearchParameterInfo _resourceTypeSearchParameterInfo;
        private readonly SearchParameterInfo _lastUpdatedSearchParameterInfo;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private DefaultFhirRequestContext _defaultFhirRequestContext;
        private readonly ISortingValidator _sortingValidator;

        public SearchOptionsFactoryTests()
        {
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _resourceTypeSearchParameterInfo = new SearchParameter { Name = SearchParameterNames.ResourceType, Code = SearchParameterNames.ResourceType, Type = SearchParamType.String }.ToInfo();
            _lastUpdatedSearchParameterInfo = new SearchParameter { Name = SearchParameterNames.LastUpdated, Code = SearchParameterNames.LastUpdated, Type = SearchParamType.String }.ToInfo();
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>()).Throws(ci => new SearchParameterNotSupportedException(ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.ResourceType).Returns(_resourceTypeSearchParameterInfo);
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.LastUpdated).Returns(_lastUpdatedSearchParameterInfo);
            _coreFeatures = new CoreFeatureConfiguration();
            _defaultFhirRequestContext = new DefaultFhirRequestContext();

            _sortingValidator = Substitute.For<ISortingValidator>();

            _factory = new SearchOptionsFactory(
                _expressionParser,
                () => searchParameterDefinitionManager,
                new OptionsWrapper<CoreFeatureConfiguration>(_coreFeatures),
                _defaultFhirRequestContext.SetupAccessor(),
                _sortingValidator,
                NullLogger<SearchOptionsFactory>.Instance);
        }

        [Fact]
        public void GivenANullQueryParameters_WhenCreated_ThenDefaultSearchOptionsShouldBeCreated()
        {
            SearchOptions options = CreateSearchOptions(queryParameters: null);

            Assert.NotNull(options);

            Assert.Null(options.ContinuationToken);
            Assert.Equal(_coreFeatures.DefaultItemCountPerSearch, options.MaxItemCount);
            ValidateResourceTypeSearchParameterExpression(options.Expression, DefaultResourceType);
        }

        [Fact]
        public void GivenMultipleContinuationTokens_WhenCreated_ThenExceptionShouldBeThrown()
        {
            const string encodedContinuationToken = "MTIz";

            Assert.Throws<InvalidSearchOperationException>(() => CreateSearchOptions(
                queryParameters: new[]
                {
                    Tuple.Create(ContinuationTokenParamName, encodedContinuationToken),
                    Tuple.Create(ContinuationTokenParamName, encodedContinuationToken),
                }));
        }

        [Fact]
        public void GivenACount_WhenCreated_ThenCorrectMaxItemCountShouldBeSet()
        {
            SearchOptions options = CreateSearchOptions(
                queryParameters: new[]
                {
                    Tuple.Create("_count", "5"),
                });

            Assert.NotNull(options);
            Assert.Equal(5, options.MaxItemCount);
        }

        [Fact]
        public void GivenNoneOfTheSearchParamIsSupported_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
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

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            ValidateResourceTypeSearchParameterExpression(options.Expression, resourceType.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenASearchParamWithEmptyValue_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedList(string value)
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName = "address-city";

            var queryParameters = new[]
            {
                Tuple.Create(paramName, value),
            };

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.Equal(queryParameters, options.UnsupportedSearchParams);
        }

        [Fact]
        public void GivenASearchParameterWithEmptyKey_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(string.Empty, "city"),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters: queryParameters);
            Assert.NotNull(options);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public void GivenSearchParametersWithEmptyKey_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create("patient", "city"),
                Tuple.Create(string.Empty, "anotherCity"),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.Equal(1, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public void GivenSearchParametersWithEmptyKeyEmptyValue_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(" ", "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Equal(2, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams.Take(1));
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams.Skip(1).Take(1));
        }

        [Fact]
        public void GivenSearchParametersWithEmptyKeyEmptyValueWithAnotherValidParameter_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create("patient", "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Equal(1, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public void GivenSearchParametersWithEmptyKeyEmptyValueWithAnotherInvalidParameter_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(string.Empty, "city"),
                Tuple.Create(string.Empty, string.Empty),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.NotNull(options.UnsupportedSearchParams);
            Assert.Equal(2, options.UnsupportedSearchParams.Count);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams.Take(1));
            Assert.Equal(queryParameters.Skip(1).Take(1), options.UnsupportedSearchParams.Skip(1).Take(1));
        }

        [Fact]
        public void GivenASearchParamWithInvalidValue_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedList()
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

            SearchOptions options = CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.Equal(queryParameters.Take(1), options.UnsupportedSearchParams);
        }

        [Fact]
        public void GivenSearchWithUnsupportedSortValue_WhenCreated_ThenSortingShouldBeEmptyAndOperationOutcomeIssueCreated()
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

            SearchOptions options = CreateSearchOptions(
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
        public void GivenSearchWithSupportedSortValue_WhenCreated_ThenSearchParamShouldBeAddedToSortList(string paramName, SortOrder sortOrder)
        {
            _sortingValidator.ValidateSorting(default, out var errors).ReturnsForAnyArgs(true);

            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Sort, paramName),
            };

            SearchOptions options = CreateSearchOptions(
                resourceType: "Patient",
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Sort);
            Assert.Equal((_lastUpdatedSearchParameterInfo, sortOrder), Assert.Single(options.Sort));
        }

        [Fact]
        public void GivenSearchWithAnInvalidSortValue_WhenCreated_ThenAnOperationOutcomeIssueIsCreated()
        {
            const string paramName = "unknownParameter";

            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Sort, paramName),
            };

            SearchOptions options = CreateSearchOptions(
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
        public void GivenAValidCompartmentSearch_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(ResourceType resourceType, CompartmentType compartmentType, string compartmentId)
        {
            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: null,
                compartmentType.ToString(),
                compartmentId);

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
        public void GivenAValidCompartmentSearchWithNullResourceType_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(CompartmentType compartmentType, string compartmentId)
        {
            SearchOptions options = CreateSearchOptions(
                resourceType: null,
                queryParameters: null,
                compartmentType.ToString(),
                compartmentId);

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
        public void GivenInvalidCompartmentType_WhenCreated_ThenExceptionShouldBeThrown(string invalidCompartmentType)
        {
            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(() => CreateSearchOptions(
                resourceType: null,
                queryParameters: null,
                invalidCompartmentType,
                "123"));

            Assert.Equal(exception.Message, $"Compartment type {invalidCompartmentType} is invalid.");
        }

        [Theory]
        [InlineData("    ")]
        [InlineData("")]
        [InlineData("       ")]
        [InlineData("\t\t")]
        public void GivenInvalidCompartmentId_WhenCreated_ThenExceptionShouldBeThrown(string invalidCompartmentId)
        {
            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(() => CreateSearchOptions(
                resourceType: ResourceType.Claim.ToString(),
                queryParameters: null,
                CompartmentType.Patient.ToString(),
                invalidCompartmentId));

            Assert.Equal("Compartment id is null or empty.", exception.Message);
        }

        [Theory]
        [InlineData(TotalType.Accurate)]
        [InlineData(TotalType.None)]
        public void GivenNoTotalParameter_WhenCreated_ThenDefaultSearchOptionsShouldHaveCountWhenConfiguredByDefault(TotalType type)
        {
            _coreFeatures.IncludeTotalInBundle = type;

            SearchOptions options = CreateSearchOptions(queryParameters: null);

            Assert.Equal(type, options.IncludeTotal);
        }

        [Fact]
        public void GivenTotalParameter_WhenCreated_ThenDefaultSearchOptionsShouldOverrideDefault()
        {
            _coreFeatures.IncludeTotalInBundle = TotalType.Accurate;

            SearchOptions options = CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_total", "none"), });

            Assert.Equal(TotalType.None, options.IncludeTotal);
        }

        [Fact]
        public void GivenNoTotalParameterWithInvalidDefault_WhenCreated_ThenDefaultSearchOptionsThrowException()
        {
            _coreFeatures.IncludeTotalInBundle = TotalType.Estimate;

            Assert.Throws<SearchOperationNotSupportedException>(() => CreateSearchOptions(queryParameters: null));
        }

        [Fact]
        public void GivenNoCountParameter_WhenCreated_ThenDefaultSearchOptionShouldUseConfigurationValue()
        {
            _coreFeatures.MaxItemCountPerSearch = 10;
            _coreFeatures.DefaultItemCountPerSearch = 3;

            SearchOptions options = CreateSearchOptions();
            Assert.Equal(3, options.MaxItemCount);
        }

        [Fact]
        public void GivenCountParameterBelowThanMaximumAllowed_WhenCreated_ThenDefaultSearchOptionShouldBeCreatedAndCountParameterShouldBeUsed()
        {
            _coreFeatures.MaxItemCountPerSearch = 20;
            _coreFeatures.DefaultItemCountPerSearch = 1;

            SearchOptions options = CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_count", "10"), });
            Assert.Equal(10, options.MaxItemCount);
        }

        [Fact]
        public void GivenCountParameterAboveThanMaximumAllowed_WhenCreated_ThenSearchOptionsAddIssueToContext()
        {
            _coreFeatures.MaxItemCountPerSearch = 10;
            _coreFeatures.DefaultItemCountPerSearch = 1;

            CreateSearchOptions(queryParameters: new[] { Tuple.Create<string, string>("_count", "11"), });

            Assert.Collection(_defaultFhirRequestContext.BundleIssues, issue => issue.Diagnostics.Contains("exceeds limit"));
        }

        [Fact]
        public void GivenSetCoreFeatureForIncludeCount_WhenCreated_ThenSearchOptionsHaveSameValue()
        {
            _coreFeatures.DefaultIncludeCountPerSearch = 9;

            SearchOptions options = CreateSearchOptions();
            Assert.Equal(_coreFeatures.DefaultIncludeCountPerSearch, options.IncludeCount);
        }

        [Fact]
        public void GivenSearchParameterText_WhenCreated_ThenSearchParameterShouldBeAddedToUnsupportedList()
        {
            var queryParameters = new[]
            {
                Tuple.Create(KnownQueryParameterNames.Text, "mobile"),
            };

            SearchOptions options = CreateSearchOptions(ResourceType.Patient.ToString(), queryParameters);
            Assert.NotNull(options);
            Assert.Equal(1, options.UnsupportedSearchParams.Count);
        }

        private SearchOptions CreateSearchOptions(
            string resourceType = DefaultResourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters = null,
            string compartmentType = null,
            string compartmentId = null)
        {
            return _factory.Create(compartmentType, compartmentId, resourceType, queryParameters);
        }
    }
}
