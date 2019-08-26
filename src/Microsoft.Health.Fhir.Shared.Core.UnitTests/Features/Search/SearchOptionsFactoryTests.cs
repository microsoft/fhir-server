// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    /// <summary>
    /// Test class for SearchOptionsFactory.Create
    /// </summary>
    public partial class SearchOptionsFactoryTests
    {
        private const string DefaultResourceType = "Patient";
        private const string ContinuationTokenParamName = "ct";

        private readonly IExpressionParser _expressionParser = Substitute.For<IExpressionParser>();
        private readonly SearchOptionsFactory _factory;
        private readonly SearchParameterInfo _resourceTypeSearchParameterInfo;

        public SearchOptionsFactoryTests()
        {
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _resourceTypeSearchParameterInfo = new SearchParameter { Name = SearchParameterNames.ResourceType, Type = SearchParamType.String }.ToInfo();
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>()).Throws(ci => new SearchParameterNotSupportedException(ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.ResourceType).Returns(_resourceTypeSearchParameterInfo);

            _factory = new SearchOptionsFactory(
                _expressionParser,
                searchParameterDefinitionManager,
                NullLogger<SearchOptionsFactory>.Instance);
        }

        [Fact]
        public void GivenANullQueryParameters_WhenCreated_ThenDefaultSearchOptionsShouldBeCreated()
        {
            SearchOptions options = CreateSearchOptions(queryParameters: null);

            Assert.NotNull(options);

            Assert.Null(options.ContinuationToken);
            Assert.Equal(10, options.MaxItemCount);
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

            _expressionParser.Parse(resourceType.ToString(), paramName1, value1).Returns(
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
            const string value1 = "abcde";
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
        public void GivenSearchWithSortValue_WhenCreated_ThenSearchParamShouldBeAddedToSortList()
        {
            const string paramName = SearchParameterNames.ResourceType;

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
            Assert.Equal(2, options.Sort.Count());
            Assert.Equal((_resourceTypeSearchParameterInfo, Core.Features.Search.SortOrder.Ascending), options.Sort.First());
            Assert.Equal((_resourceTypeSearchParameterInfo, Core.Features.Search.SortOrder.Descending), options.Sort.Last());
        }

        [Fact]
        public void GivenSearchWithAnInvalidSortValue_WhenCreated_ThenSearchParamShouldBeAddedToUnsupportedSortingList()
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

            Assert.Equal(1, options.UnsupportedSortingParams.Count);
            Assert.Equal(paramName, options.UnsupportedSortingParams.First().parameterName);
        }

        [Theory]
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
