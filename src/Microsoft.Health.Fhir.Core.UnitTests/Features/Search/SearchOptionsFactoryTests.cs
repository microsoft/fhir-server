// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
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
            _factory = new SearchOptionsFactory(
                _expressionParser,
                NullLogger<SearchOptionsFactory>.Instance);
        }

        [Fact]
        public void GivenANullQueryParameters_WhenCreated_ThenDefaultSearchOptionsShouldBeCreated()
        {
            SearchOptions options = CreateSearchOptions(queryParameters: null);

            Assert.NotNull(options);
            Assert.Equal(DefaultResourceType, options.ResourceType);
            Assert.Null(options.ContinuationToken);
            Assert.Equal(10, options.MaxItemCount);
            Assert.Null(options.Expression);
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

            ValidateMultiaryExpression(options.Expression, MultiaryOperator.And, e => Assert.Equal(expression, e));
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
            Assert.Null(options.Expression);
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

        private SearchOptions CreateSearchOptions(
            string resourceType = DefaultResourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters = null)
        {
            return _factory.Create(resourceType, queryParameters);
        }
    }
}
