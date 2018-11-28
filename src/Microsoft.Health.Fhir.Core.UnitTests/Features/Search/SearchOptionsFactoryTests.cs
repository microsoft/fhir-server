// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchOptionsFactoryTests
    {
        private const string DefaultResourceType = "Patient";
        private const string ContinuationTokenParamName = "ct";

        private readonly IExpressionParser _expressionParser = Substitute.For<IExpressionParser>();
        private readonly SearchOptionsFactory _factory;

        public SearchOptionsFactoryTests()
        {
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameter(ResourceType.Resource, SearchParameterNames.ResourceType).Returns(new SearchParameter { Name = SearchParameterNames.ResourceType });

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
            ValidateSearchParameterExpression(options.Expression, SearchParameterNames.ResourceType, e => ValidateBinaryExpression(e, FieldName.TokenCode, BinaryOperator.Equal, DefaultResourceType));
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
        public void GivenASupportedSearchParam_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName = "address-city";
            const string value = "Seattle";

            Expression expression = Substitute.For<Expression>();

            _expressionParser.Parse(resourceType, paramName, value).Returns(expression);

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: new[] { Tuple.Create(paramName, value) });

            Assert.NotNull(options);
            Assert.NotNull(options.Expression);

            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => Assert.Equal(expression, e));
        }

        [Fact]
        public void GivenMultipleSupportedSearchParams_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName1 = "address-city";
            const string paramName2 = "address-state";
            const string value1 = "Seattle";
            const string value2 = "WA";

            Expression expression1 = Substitute.For<Expression>();
            Expression expression2 = Substitute.For<Expression>();

            _expressionParser.Parse(resourceType, paramName1, value1).Returns(expression1);
            _expressionParser.Parse(resourceType, paramName2, value2).Returns(expression2);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
            };

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Expression);

            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => Assert.Equal(expression1, e),
                e => Assert.Equal(expression2, e));
        }

        [Fact]
        public void GivenANotSupportedSearchParam_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName1 = "address-city";
            const string paramName2 = "not-supported-search-param";
            const string paramName3 = "address-state";
            const string value1 = "Seattle";
            const string value2 = "Test";
            const string value3 = "WA";

            Expression expression1 = Substitute.For<Expression>();
            Expression expression3 = Substitute.For<Expression>();

            _expressionParser.Parse(resourceType, paramName1, value1).Returns(expression1);
            _expressionParser.Parse(resourceType, paramName2, value2)
                .Returns(x => throw new SearchParameterNotSupportedException(x.ArgAt<ResourceType>(0), x.ArgAt<string>(1)));
            _expressionParser.Parse(resourceType, paramName3, value3).Returns(expression3);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
                Tuple.Create(paramName3, value3),
            };

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters);

            Assert.NotNull(options);
            Assert.NotNull(options.Expression);

            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => Assert.Equal(expression1, e),
                e => Assert.Equal(expression3, e));
        }

        [Fact]
        public void GivenNoneOfTheSearchParamIsSupported_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName1 = "address-city";
            const string value1 = "Seattle";

            _expressionParser.Parse(resourceType, paramName1, value1).Returns(
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
                e => ValidateCompartmentSearchExpression(e, compartmentType, compartmentId));
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
            ValidateCompartmentSearchExpression(options.Expression, compartmentType, compartmentId);
        }

        [Theory]
        [InlineData(ResourceType.Patient, CompartmentType.Patient, "123")]
        [InlineData(ResourceType.Appointment, CompartmentType.Device, "abc")]
        [InlineData(ResourceType.Patient, CompartmentType.Encounter, "aaa")]
        [InlineData(ResourceType.Condition, CompartmentType.Practitioner, "945934-5934")]
        [InlineData(ResourceType.Patient, CompartmentType.RelatedPerson, "hgdfhdfgdf")]
        [InlineData(ResourceType.Claim, CompartmentType.Encounter, "ksd;/fkds;kfsd;kf")]
        public void GivenSearchParamsWithValidCompartmentSearch_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(ResourceType resourceType, CompartmentType compartmentType, string compartmentId)
        {
            const string paramName1 = "address-city";
            const string paramName2 = "address-state";
            const string value1 = "Seattle";
            const string value2 = "WA";

            Expression expression1 = Substitute.For<Expression>();
            Expression expression2 = Substitute.For<Expression>();

            _expressionParser.Parse(resourceType, paramName1, value1).Returns(expression1);
            _expressionParser.Parse(resourceType, paramName2, value2).Returns(expression2);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
            };

            SearchOptions options = CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters,
                compartmentType.ToString(),
                compartmentId);

            Assert.NotNull(options);
            Assert.NotNull(options.Expression);

            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => Assert.Equal(expression1, e),
                e => Assert.Equal(expression2, e),
                e => ValidateCompartmentSearchExpression(e, compartmentType, compartmentId));
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
