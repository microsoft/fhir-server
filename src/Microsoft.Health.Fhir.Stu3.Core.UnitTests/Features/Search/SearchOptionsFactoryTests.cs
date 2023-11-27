// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    /// <summary>
    /// Test class for STU3 Search Expressions
    /// </summary>
    public partial class SearchOptionsFactoryTests
    {
        [Fact]
        public async void GivenASupportedSearchParam_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName = "address-city";
            const string value = "Seattle";

            Expression expression = Substitute.For<Expression>();

            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName, value).Returns(expression);

            SearchOptions options = await CreateSearchOptions(
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
        public async void GivenMultipleSupportedSearchParams_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string paramName1 = "address-city";
            const string paramName2 = "address-state";
            const string value1 = "Seattle";
            const string value2 = "WA";

            Expression expression1 = Substitute.For<Expression>();
            Expression expression2 = Substitute.For<Expression>();

            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName1, value1).Returns(expression1);
            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName2, value2).Returns(expression2);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
            };

            SearchOptions options = await CreateSearchOptions(
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
        public async void GivenANotSupportedSearchParam_WhenCreated_ThenCorrectExpressionShouldBeGenerated()
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

            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName1, value1).Returns(expression1);
            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName2, value2)
                .Returns(x => throw new SearchParameterNotSupportedException(x.ArgAt<string[]>(0)[0], x.ArgAt<string>(1)));
            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName3, value3).Returns(expression3);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
                Tuple.Create(paramName3, value3),
            };

            SearchOptions options = await CreateSearchOptions(
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

        [Theory]
        [InlineData(ResourceType.Patient, CompartmentType.Patient, "123")]
        [InlineData(ResourceType.Appointment, CompartmentType.Device, "abc")]
        [InlineData(ResourceType.Patient, CompartmentType.Encounter, "aaa")]
        [InlineData(ResourceType.Condition, CompartmentType.Practitioner, "945934-5934")]
        [InlineData(ResourceType.Patient, CompartmentType.RelatedPerson, "hgdfhdfgdf")]
        [InlineData(ResourceType.Claim, CompartmentType.Encounter, "ksd;/fkds;kfsd;kf")]
        public async void GivenSearchParamsWithValidCompartmentSearch_WhenCreated_ThenCorrectCompartmentSearchExpressionShouldBeGenerated(ResourceType resourceType, CompartmentType compartmentType, string compartmentId)
        {
            const string paramName1 = "address-city";
            const string paramName2 = "address-state";
            const string value1 = "Seattle";
            const string value2 = "WA";

            Expression expression1 = Substitute.For<Expression>();
            Expression expression2 = Substitute.For<Expression>();

            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName1, value1).Returns(expression1);
            _expressionParser.Parse(Arg.Is<string[]>(x => x.Length == 1 && x[0] == resourceType.ToString()), paramName2, value2).Returns(expression2);

            var queryParameters = new[]
            {
                Tuple.Create(paramName1, value1),
                Tuple.Create(paramName2, value2),
            };

            SearchOptions options = await CreateSearchOptions(
                resourceType: resourceType.ToString(),
                queryParameters: queryParameters,
                compartmentType: compartmentType.ToString(),
                compartmentId: compartmentId);

            Assert.NotNull(options);
            Assert.NotNull(options.Expression);

            ValidateMultiaryExpression(
                options.Expression,
                MultiaryOperator.And,
                e => ValidateResourceTypeSearchParameterExpression(e, resourceType.ToString()),
                e => Assert.Equal(expression1, e),
                e => Assert.Equal(expression2, e),
                e => ValidateCompartmentSearchExpression(e, compartmentType.ToString(), compartmentId));
        }
    }
}
