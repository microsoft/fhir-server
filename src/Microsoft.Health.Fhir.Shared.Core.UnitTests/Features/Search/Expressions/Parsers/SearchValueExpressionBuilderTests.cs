// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;
using Expression=Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using SearchParamType = Hl7.Fhir.Model.SearchParamType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchValueExpressionBuilderTests
    {
        private const string DefaultParamName = "param";
        private static readonly Uri BaseUrl = new Uri("http://localhost/");

        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IReferenceSearchValueParser _referenceSearchValueParser = Substitute.For<IReferenceSearchValueParser>();

        private readonly SearchParameterExpressionParser _parser;

        public SearchValueExpressionBuilderTests()
        {
            _parser = new SearchParameterExpressionParser(_referenceSearchValueParser);
        }

        public static IEnumerable<object[]> GetNonEqualSearchComparatorAsMemberData()
        {
            return GetEnumAsMemberData<SearchComparator>(comparator => comparator != SearchComparator.Eq);
        }

        public static IEnumerable<object[]> GetNoneTokenSearchParamTypeAsMemberData()
        {
            return GetEnumAsMemberData<SearchParamType>(t => t != SearchParamType.Token);
        }

        public static IEnumerable<object[]> GetAllModifiersExceptMissing()
        {
            return Enum.GetValues(typeof(SearchModifierCode))
                .Cast<SearchModifierCode>()
                .Where(modifier => modifier != SearchModifierCode.Missing)
                .Select(modifier => new object[] { new SearchModifier(modifier, modifier == SearchModifierCode.Type ? ResourceType.Patient.ToString() : null) });
        }

        public static IEnumerable<object[]> GetAllModifiersExceptMissingOrType()
        {
            return Enum.GetValues(typeof(SearchModifierCode))
                .Cast<SearchModifierCode>()
                .Where(modifier => modifier != SearchModifierCode.Missing && modifier != SearchModifierCode.Type)
                .Select(modifier => new object[] { new SearchModifier(modifier) });
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void GivenMissingModifierIsSpecified_WhenBuilt_ThenMissingExpressionShouldBeCreated(string isMissingString, bool expectedIsMissing)
        {
            Expression expression = _parser.Parse(
                CreateSearchParameter(SearchParamType.String),
                new SearchModifier(SearchModifierCode.Missing),
                isMissingString);

            ValidateMissingParamExpression(expression, DefaultParamName, expectedIsMissing);
        }

        [Fact]
        public void GivenMissingModifierWithAnInvalidValue_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown()
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.String), new SearchModifier(SearchModifierCode.Missing), "test"));
        }

        [Fact]
        public void GivenMultipleValues_WhenBuilding_ThenCorrectExpressionShouldBeCreated()
        {
            const string value1 = "value1";
            const string value2 = "value2";
            string value = $"{value1},{value2}";

            // Parse the expression.
            Validate(
                CreateSearchParameter(SearchParamType.String),
                null,
                value,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.Or,
                    nestedExpression => ValidateStringExpression(nestedExpression, FieldName.String, StringOperator.StartsWith, value1, true),
                    nestedExpression => ValidateStringExpression(nestedExpression, FieldName.String, StringOperator.StartsWith, value2, true)));
        }

        [Theory]
        [InlineData(SearchParamType.Date, "lt2018,2019")]
        [InlineData(SearchParamType.Number, "gt10,11")]
        [InlineData(SearchParamType.Quantity, "ne15|s|c,5|s|c")]
        public void GivenMultipleValuesWithPrefix_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchParamType searchParameterType, string value)
        {
            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _parser.Parse(
                CreateSearchParameter(searchParameterType),
                null,
                value));
        }

        [Fact]
        public void GivenACompositeExceedingNumberOfComponents_WhenBuilt_ThenInvalidSearchOperationExceptionShouldBeThrown()
        {
            SearchParameterComponentInfo[] components = new[] { new SearchParameterComponentInfo() };
            var searchParameter1 = new SearchParameterInfo(
                DefaultParamName,
                DefaultParamName,
                Microsoft.Health.Fhir.ValueSets.SearchParamType.Composite,
                components: components);
            SearchParameterInfo searchParameter = searchParameter1;

            Assert.Throws<InvalidSearchOperationException>(() => _parser.Parse(searchParameter, null, "a$b$c"));
        }

        [Fact]
        public void GivenACompositeWithVariousTypes_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const decimal quantity = 10;
            const string quantitySystem = "quantity-system";
            const string quantityCode = "quantity-code";

            var codeUri = new Uri("http://code");
            var quantityUri = new Uri("http://quantity");

            SearchParameterComponentInfo[] components =
            {
                new SearchParameterComponentInfo(codeUri) { ResolvedSearchParameter = new SearchParameterInfo("code", "code", ValueSets.SearchParamType.Token) },
                new SearchParameterComponentInfo(quantityUri) { ResolvedSearchParameter = new SearchParameterInfo("quantity", "quantity", ValueSets.SearchParamType.Quantity) },
            };

            SearchParameterInfo searchParameter = new SearchParameterInfo(
                DefaultParamName,
                DefaultParamName,
                Microsoft.Health.Fhir.ValueSets.SearchParamType.Composite,
                components: components);

            Validate(
                searchParameter,
                null,
                $"{system}|{code}${quantity}|{quantitySystem}|{quantityCode}",
                outer => ValidateMultiaryExpression(
                    outer,
                    MultiaryOperator.And,
                    e => ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        se => ValidateStringExpression(se, FieldName.TokenSystem, StringOperator.Equals, system, false),
                        se => ValidateStringExpression(se, FieldName.TokenCode, StringOperator.Equals, code, false)),
                    e => ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, quantitySystem, false),
                        e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, quantityCode, false),
                        e1 => ValidateMultiaryExpression(
                            e1,
                            MultiaryOperator.And,
                            e2 => ValidateBinaryExpression(e2, FieldName.Quantity, BinaryOperator.GreaterThanOrEqual, 9.5m),
                            e2 => ValidateBinaryExpression(e2, FieldName.Quantity, BinaryOperator.LessThanOrEqual, 10.5m)))));
        }

        [Fact]
        public void GivenACompositeWithIndividualPrefix_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string quantitySystem1 = "qs1";
            const string quantityCode1 = "qc1";
            const decimal quantity1 = 10m;
            const string quantitySystem2 = "qs2";
            const string quantityCode2 = "qc2";
            const decimal quantity2 = 25.3m;

            var quantityUri = new Uri("http://quantity");

            SearchParameterComponentInfo[] components =
            {
                new SearchParameterComponentInfo(quantityUri) { ResolvedSearchParameter = new SearchParameterInfo("quantity", "quantity", ValueSets.SearchParamType.Quantity) },
                new SearchParameterComponentInfo(quantityUri) { ResolvedSearchParameter = new SearchParameterInfo("quantity", "quantity", ValueSets.SearchParamType.Quantity) },
            };

            SearchParameterInfo searchParameter = new SearchParameterInfo(
                DefaultParamName,
                DefaultParamName,
                Microsoft.Health.Fhir.ValueSets.SearchParamType.Composite,
                components: components);

            Validate(
                searchParameter,
                null,
                $"gt{quantity1}|{quantitySystem1}|{quantityCode1}$le{quantity2}|{quantitySystem2}|{quantityCode2}",
                outer => ValidateMultiaryExpression(
                    outer,
                    MultiaryOperator.And,
                    e => ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, quantitySystem1, false),
                        e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, quantityCode1, false),
                        e1 => ValidateBinaryExpression(e1, FieldName.Quantity, BinaryOperator.GreaterThan, quantity1)),
                    e => ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, quantitySystem2, false),
                        e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, quantityCode2, false),
                        e1 => ValidateBinaryExpression(e1, FieldName.Quantity, BinaryOperator.LessThanOrEqual, quantity2))));
        }

        [Theory]
        [MemberData(nameof(GetAllModifiersExceptMissing))]
        public void GivenACompositeWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifier modifier)
        {
            var quantityUri = new Uri("http://quantity");

            SearchParameterComponentInfo[] components = new[] { new SearchParameterComponentInfo(quantityUri), new SearchParameterComponentInfo(quantityUri) };
            var searchParameter1 = new SearchParameterInfo(
                DefaultParamName,
                DefaultParamName,
                Microsoft.Health.Fhir.ValueSets.SearchParamType.Composite,
                components: components);
            SearchParameterInfo searchParameter = searchParameter1;

            _searchParameterDefinitionManager.GetSearchParameter(quantityUri.OriginalString).Returns(
                new SearchParameter
                {
                    Name = "quantity",
                    Code = "quantity",
                    Type = SearchParamType.Quantity,
                }.ToInfo());

            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Composite), modifier, "10|s|c$10|s|c"));
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateWithNoComparator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string input)
        {
            var partialDateTime = PartialDateTime.Parse(input);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            Validate(
                CreateSearchParameter(SearchParamType.Date),
                null,
                input,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, dateTimeSearchValue.Start),
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateWithEqComparator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string dateTimeInput)
        {
            var partialDateTime = PartialDateTime.Parse(dateTimeInput);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            Validate(
                CreateSearchParameter(SearchParamType.Date),
                null,
                "eq" + dateTimeInput,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, dateTimeSearchValue.Start),
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateWithNeComparator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string dateTimeInput)
        {
            var partialDateTime = PartialDateTime.Parse(dateTimeInput);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            Validate(
                CreateSearchParameter(SearchParamType.Date),
                null,
                "ne" + dateTimeInput,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.Or,
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.LessThan, dateTimeSearchValue.Start),
                    e2 => ValidateDateTimeBinaryOperatorExpression(e2, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("lt", "2018", FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("lt", "2018-02", FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("lt", "2018-02-01", FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("lt", "2018-02-01T10:00", FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("lt", "2018-02-01T10:00-07:00", FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("gt", "2018", FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("gt", "2018-02", FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("gt", "2018-02-01", FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("gt", "2018-02-01T10:00", FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("gt", "2018-02-01T10:00-07:00", FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("le", "2018", FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("le", "2018-02", FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("le", "2018-02-01", FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("le", "2018-02-01T10:00", FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("le", "2018-02-01T10:00-07:00", FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("ge", "2018", FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("ge", "2018-02", FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("ge", "2018-02-01", FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("ge", "2018-02-01T10:00", FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("ge", "2018-02-01T10:00-07:00", FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("sa", "2018", FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("sa", "2018-02", FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("sa", "2018-02-01", FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("sa", "2018-02-01T10:00", FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("sa", "2018-02-01T10:00-07:00", FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("eb", "2018", FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("eb", "2018-02", FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("eb", "2018-02-01", FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("eb", "2018-02-01T10:00", FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("eb", "2018-02-01T10:00-07:00", FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        public void GivenADateWithComparatorOfSingleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string prefix, string dateTimeInput, FieldName fieldName, BinaryOperator binaryOperator, bool expectStartTimeValue)
        {
            var partialDateTime = PartialDateTime.Parse(dateTimeInput);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            Validate(
                CreateSearchParameter(SearchParamType.Date),
                null,
                prefix + dateTimeInput,
                e => ValidateDateTimeBinaryOperatorExpression(
                    e,
                    fieldName,
                    binaryOperator,
                    expectStartTimeValue ? dateTimeSearchValue.Start : dateTimeSearchValue.End));
        }

#if NET8_0_OR_GREATER
        [Theory]
        [InlineData("2016", "2015-11-25T12:00:00.0000000+00:00", "2017-02-06T11:59:59.9999999+00:00")]
        [InlineData("2016-02", "2015-11-25T21:36:00.0000000+00:00", "2016-05-07T02:23:59.9999999+00:00")]
        [InlineData("2016-02-01", "2015-11-23T02:24:00.0000000+00:00", "2016-04-11T21:35:59.9999999+00:00")]
        [InlineData("2016-02-01T10:00", "2015-11-23T11:00:06.0000000+00:00", "2016-04-11T09:00:53.9999999+00:00")]
        [InlineData("2016-02-01T10:00-07:00", "2015-11-23T18:42:06.0000000+00:00", "2016-04-11T15:18:53.9999999+00:00")]
        [InlineData("2220", "2240-04-19T09:35:59.9999999+00:00", "2200-09-13T14:24:00.0000000+00:00")]
        [InlineData("2220-02", "2240-04-19T19:11:59.9999999+00:00", "2199-12-12T04:48:00.0000000+00:00")]
        [InlineData("2220-02-01", "2240-04-16T23:59:59.9999999+00:00", "2199-11-17T00:00:00.0000000+00:00")]
        [InlineData("2220-02-01T10:00", "2240-04-17T08:36:05.9999999+00:00", "2199-11-16T11:24:54.0000000+00:00")]
        [InlineData("2220-02-01T10:00-07:00", "2240-04-17T16:18:05.9999999+00:00", "2199-11-16T17:42:54.0000000+00:00")]
        public void GivenADateWithApComparator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string dateTimeInput, string expectedStartValue, string expectedEndValue)
        {
            using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2018-01-01T00:00Z"))))
            {
                var partialDateTime = PartialDateTime.Parse(dateTimeInput);
                var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

                Validate(
                    CreateSearchParameter(SearchParamType.Date),
                    null,
                    "ap" + dateTimeInput,
                    e => ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, DateTimeOffset.Parse(expectedStartValue)),
                        e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, DateTimeOffset.Parse(expectedEndValue))));
            }
        }
#endif

        [Theory]
        [MemberData(nameof(GetAllModifiersExceptMissing))]
        public void GivenADateWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifier modifier)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Date), modifier, "1980"));
        }

        [Theory]
        [InlineData("", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 14.95, 15.05)]
        [InlineData("eq", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 14.95, 15.05)]
        [InlineData("ap", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 13.45, 16.55)]
        [InlineData("ne", MultiaryOperator.Or, BinaryOperator.LessThan, BinaryOperator.GreaterThan, 14.95, 15.05)]
        public void GivenANumberWithComparatorOfMultipleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(
            string prefix,
            MultiaryOperator multiaryOperator,
            BinaryOperator lowerBoundOperator,
            BinaryOperator upperBoundOperator,
            decimal lowerBoundValue,
            decimal upperBoundValue)
        {
            Validate(
                CreateSearchParameter(SearchParamType.Number),
                null,
                $"{prefix}15.0",
                e => ValidateMultiaryExpression(
                    e,
                    multiaryOperator,
                    e1 => ValidateBinaryExpression(e1, FieldName.Number, lowerBoundOperator, lowerBoundValue),
                    e1 => ValidateBinaryExpression(e1, FieldName.Number, upperBoundOperator, upperBoundValue)));
        }

        [Theory]
        [InlineData("gt", BinaryOperator.GreaterThan)]
        [InlineData("ge", BinaryOperator.GreaterThanOrEqual)]
        [InlineData("lt", BinaryOperator.LessThan)]
        [InlineData("le", BinaryOperator.LessThanOrEqual)]
        [InlineData("sa", BinaryOperator.GreaterThan)]
        [InlineData("eb", BinaryOperator.LessThan)]
        public void GivenANumberWithComparatorOfSingleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string prefix, BinaryOperator binaryOperator)
        {
            const decimal expected = 15.0m;

            Validate(
                CreateSearchParameter(SearchParamType.Number),
                null,
                $"{prefix}{expected}",
                e => ValidateBinaryExpression(e, FieldName.Number, binaryOperator, expected));
        }

        [Theory]
        [MemberData(nameof(GetAllModifiersExceptMissing))]
        public void GivenANumberWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifier modifier)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Number), modifier, "123"));
        }

        [Theory]
        [InlineData("", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 6.045, 6.055)]
        [InlineData("eq", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 6.045, 6.055)]
        [InlineData("ap", MultiaryOperator.And, BinaryOperator.GreaterThanOrEqual, BinaryOperator.LessThanOrEqual, 5.440, 6.66)]
        [InlineData("ne", MultiaryOperator.Or, BinaryOperator.LessThan, BinaryOperator.GreaterThan, 6.045, 6.055)]
        public void GivenAQuantityWithComparatorOfMultipleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(
            string prefix,
            MultiaryOperator multiaryOperator,
            BinaryOperator lowerBoundOperator,
            BinaryOperator upperBoundOperator,
            decimal lowerBoundValue,
            decimal upperBoundValue)
        {
            const string system = "system";
            const string code = "code";

            Validate(
                CreateSearchParameter(SearchParamType.Quantity),
                null,
                $"{prefix}6.05|{system}|{code}",
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, system, false),
                    e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, code, false),
                    e1 => ValidateMultiaryExpression(
                        e1,
                        multiaryOperator,
                        e2 => ValidateBinaryExpression(e2, FieldName.Quantity, lowerBoundOperator, lowerBoundValue),
                        e2 => ValidateBinaryExpression(e2, FieldName.Quantity, upperBoundOperator, upperBoundValue))));
        }

        [Theory]
        [InlineData("gt", BinaryOperator.GreaterThan)]
        [InlineData("ge", BinaryOperator.GreaterThanOrEqual)]
        [InlineData("lt", BinaryOperator.LessThan)]
        [InlineData("le", BinaryOperator.LessThanOrEqual)]
        [InlineData("sa", BinaryOperator.GreaterThan)]
        [InlineData("eb", BinaryOperator.LessThan)]
        public void GivenAQuantityWithComparatorOfSingleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeCreated(
            string prefix,
            BinaryOperator binaryOperator)
        {
            const string system = "system";
            const string code = "code";
            const decimal expected = 135m;

            Validate(
                CreateSearchParameter(SearchParamType.Quantity),
                null,
                $"{prefix}{expected}|{system}|{code}",
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, system, false),
                    e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, code, false),
                    e1 => ValidateBinaryExpression(e1, FieldName.Quantity, binaryOperator, expected)));
        }

        [Fact]
        public void GivenAQuantityWithEmptySystemSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string code = "code";
            const decimal expected = 135m;

            Validate(
                CreateSearchParameter(SearchParamType.Quantity),
                null,
                $"gt{expected}||{code}",
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.QuantityCode, StringOperator.Equals, code, false),
                    e1 => ValidateBinaryExpression(e1, FieldName.Quantity, BinaryOperator.GreaterThan, expected)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("|")]
        public void GivenAQuantityWithEmptyCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string suffix)
        {
            const string system = "system";
            const decimal expected = 135m;

            Validate(
                CreateSearchParameter(SearchParamType.Quantity),
                null,
                $"le{expected}|{system}{suffix}",
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.QuantitySystem, StringOperator.Equals, system, false),
                    e1 => ValidateBinaryExpression(e1, FieldName.Quantity, BinaryOperator.LessThanOrEqual, expected)));
        }

        [Fact]
        public void GivenAQuantityWithEmptySystemAndCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const decimal expected = 135m;

            Validate(
                CreateSearchParameter(SearchParamType.Quantity),
                null,
                $"gt{expected}",
                e => ValidateBinaryExpression(e, FieldName.Quantity, BinaryOperator.GreaterThan, expected));
        }

        [Theory]
        [MemberData(nameof(GetAllModifiersExceptMissing))]
        public void GivenAQuantityWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifier modifier)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Quantity), modifier, "1"));
        }

        [Fact]
        public void GivenAReferenceUsingResourceId_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string resourceId = "123";

            _referenceSearchValueParser.Parse(resourceId).Returns(new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, null, resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                null,
                resourceId,
                e => ValidateStringExpression(e, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false));
        }

        [Fact]
        public void GivenAReferenceUsingResourceTypeAndResourceIdTargetingInternalResource_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string resourceId = "123";
            string reference = $"{resourceType}/{resourceId}";

            _referenceSearchValueParser.Parse(reference).Returns(new ReferenceSearchValue(ReferenceKind.Internal, null, resourceType.ToString(), resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                null,
                reference,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateMissingFieldExpression(e1, FieldName.ReferenceBaseUri),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceType, StringOperator.Equals, resourceType.ToString(), false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false)));
        }

        [Fact]
        public void GivenAReferenceUsingResourceIdWithTargetTypeModifierTargetingInternalResource_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string resourceId = "123";

            _referenceSearchValueParser.Parse(resourceId).Returns(new ReferenceSearchValue(ReferenceKind.Internal, null, null, resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                new SearchModifier(SearchModifierCode.Type, resourceType.ToString()),
                resourceId.ToString(),
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateMissingFieldExpression(e1, FieldName.ReferenceBaseUri),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceType, StringOperator.Equals, resourceType.ToString(), false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false)));
        }

        [Theory]
        [InlineData(ReferenceKind.InternalOrExternal)]
        [InlineData(ReferenceKind.External)]
        public void GivenAReferenceUsingResourceTypeAndResourceId_WhenBuilt_ThenCorrectExpressionShouldBeCreated(ReferenceKind referenceLocations)
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string resourceId = "123";
            string reference = $"{resourceType}/{resourceId}";

            _referenceSearchValueParser.Parse(reference).Returns(new ReferenceSearchValue(referenceLocations, null, resourceType.ToString(), resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                null,
                reference,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceType, StringOperator.Equals, resourceType.ToString(), false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false)));
        }

        [Theory]
        [InlineData(ReferenceKind.InternalOrExternal, true)]
        [InlineData(ReferenceKind.InternalOrExternal, false)]
        [InlineData(ReferenceKind.External, true)]
        [InlineData(ReferenceKind.External, false)]
        public void GivenAReferenceUsingResourceIdWithTargetTypeModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated(ReferenceKind referenceLocations, bool withResourceType)
        {
            const ResourceType resourceType = ResourceType.Patient;
            const string resourceId = "123";
            string reference = $"{(withResourceType ? resourceType + "/" : null)}{resourceId}";

            _referenceSearchValueParser.Parse(reference).Returns(new ReferenceSearchValue(referenceLocations, null, withResourceType ? resourceType.ToString() : null, resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                new SearchModifier(SearchModifierCode.Type, resourceType.ToString()),
                reference,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceType, StringOperator.Equals, resourceType.ToString(), false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false)));
        }

        [Fact]
        public void GivenAReferenceUsingAbsoluteUrl_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const ResourceType resourceType = ResourceType.Account;
            const string resourceId = "xyz";
            const string baseUrl = "http://somehwere.com/stu3/";

            string reference = $"{baseUrl}{resourceType}/{resourceId}";

            _referenceSearchValueParser.Parse(reference).Returns(new ReferenceSearchValue(ReferenceKind.InternalOrExternal, new Uri(baseUrl), resourceType.ToString(), resourceId));

            Validate(
                CreateSearchParameter(SearchParamType.Reference),
                null,
                reference,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceBaseUri, StringOperator.Equals, baseUrl, false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceType, StringOperator.Equals, resourceType.ToString(), false),
                    e1 => ValidateStringExpression(e1, FieldName.ReferenceResourceId, StringOperator.Equals, resourceId, false)));
        }

        [Theory]
        [MemberData(nameof(GetAllModifiersExceptMissingOrType))]
        public void GivenAReferenceWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifier modifier)
        {
            _referenceSearchValueParser.Parse(Arg.Any<string>()).Returns(new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, null, "123"));

            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Reference), modifier, "Patient/test"));
        }

        [Theory]
        [InlineData("Patient", "Group", "test")]
        [InlineData("InvalidModifier", null, "test")]
        public void GivenAReferenceWithInvalidTargetTypeModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(string targetTypeModifier, string resourceType, string resourceId)
        {
            _referenceSearchValueParser.Parse(Arg.Any<string>()).Returns(new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, resourceType, resourceId));

            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Reference), new SearchModifier(SearchModifierCode.Type, targetTypeModifier), $"{resourceType}{(string.IsNullOrEmpty(resourceType) ? null : "/")}{resourceId}"));
        }

        [Fact]
        public void GivenAStringWithNoModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string input = "TestString123";

            Validate(
                CreateSearchParameter(SearchParamType.String),
                null,
                input,
                e => ValidateStringExpression(e, FieldName.String, StringOperator.StartsWith, input, true));
        }

        [Fact]
        public void GivenAStringWithExact_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string input = "TestString123";

            Validate(
                CreateSearchParameter(SearchParamType.String),
                new SearchModifier(SearchModifierCode.Exact),
                input,
                e => ValidateStringExpression(e, FieldName.String, StringOperator.Equals, input, false));
        }

        [Fact]
        public void GivenAStringWithContainsModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string input = "TestString123";

            Validate(
                CreateSearchParameter(SearchParamType.String),
                new SearchModifier(SearchModifierCode.Contains),
                input,
                e => ValidateStringExpression(e, FieldName.String, StringOperator.Contains, input, true));
        }

        [Theory]
        [InlineData(SearchModifierCode.Above)]
        [InlineData(SearchModifierCode.Below)]
        [InlineData(SearchModifierCode.In)]
        [InlineData(SearchModifierCode.Not)]
        [InlineData(SearchModifierCode.NotIn)]
        [InlineData(SearchModifierCode.Text)]
        [InlineData(SearchModifierCode.Type, ResourceType.Patient)]
        public void GivenAStringWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifierCode modifier, ResourceType? targetTypeModifier = null)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.String), new SearchModifier(modifier, targetTypeModifier?.ToString()), "test"));
        }

        [Fact]
        public void GivenATokenWithTextModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string input = "TestString123";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                new SearchModifier(SearchModifierCode.Text),
                input,
                e => ValidateStringExpression(e, FieldName.TokenText, StringOperator.StartsWith, input, true));
        }

        [Fact]
        public void GivenATokenWithNotModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string input = "TestString123";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                new SearchModifier(SearchModifierCode.Not),
                input,
                e => ValidateNotExpression(e, x => ValidateStringExpression(x, FieldName.TokenCode, StringOperator.Equals, input, false)));
        }

        [Theory]
        [MemberData(nameof(GetNoneTokenSearchParamTypeAsMemberData))]
        public void GivenASearchParameterThatDoesNotSupportTextModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated(SearchParamType searchParameterType)
        {
            var searchParameter = new SearchParameter
            {
                Name = "test",
                Code = "test",
                Type = searchParameterType,
            };

            Assert.Throws<InvalidSearchOperationException>(() => _parser.Parse(searchParameter.ToInfo(), new SearchModifier(SearchModifierCode.Text), "test"));
        }

        [Fact]
        public void GivenATokenWithNoSystemSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string code = "code";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                null,
                code,
                e => ValidateStringExpression(e, FieldName.TokenCode, StringOperator.Equals, code, false));
        }

        [Fact]
        public void GivenATokenWithEmptySystemSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string code = "code";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                null,
                $"|{code}",
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    childExpression => ValidateMissingFieldExpression(childExpression, FieldName.TokenSystem),
                    childExpression => ValidateStringExpression(childExpression, FieldName.TokenCode, StringOperator.Equals, code, false)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenATokenWithEmptyCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated(string code)
        {
            const string system = "system";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                null,
                $"{system}|{code}",
                e => ValidateStringExpression(e, FieldName.TokenSystem, StringOperator.Equals, system, false));
        }

        [Fact]
        public void GivenATokenWithSystemAndCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";

            Validate(
                CreateSearchParameter(SearchParamType.Token),
                null,
                $"{system}|{code}",
                e =>
                {
                    ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        childExpression => ValidateStringExpression(childExpression, FieldName.TokenSystem, StringOperator.Equals, system, false),
                        childExpression => ValidateStringExpression(childExpression, FieldName.TokenCode, StringOperator.Equals, code, false));
                });
        }

        [Theory]
        [InlineData(SearchModifierCode.Exact)]
        [InlineData(SearchModifierCode.Contains)]
        [InlineData(SearchModifierCode.Type, ResourceType.Patient)]
        public void GivenATokenWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifierCode modifier, ResourceType? targetTypeModifier = null)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Token), new SearchModifier(modifier, targetTypeModifier?.ToString()), "test"));
        }

        [Fact]
        public void GivenAUriWithNoModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            string input = "http://example.com/valueset";

            Validate(
                CreateSearchParameter(SearchParamType.Uri),
                null,
                input,
                e => ValidateStringExpression(e, FieldName.Uri, StringOperator.Equals, input, false));
        }

        [Fact]
        public void GivenAUriWithAboveModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            string input = "http://example.com/valueset";

            Validate(
                CreateSearchParameter(SearchParamType.Uri),
                new SearchModifier(SearchModifierCode.Above),
                input,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.Uri, StringOperator.LeftSideStartsWith, input, false),
                    e2 => ValidateStringExpression(e2, FieldName.Uri, StringOperator.NotStartsWith, "urn:", false)));
        }

        [Fact]
        public void GivenAUriWithBelowModifier_WhenBuilt_ThenCorrectExpressionShouldBeCreated()
        {
            string input = "http://example.com/valueset";

            Validate(
                CreateSearchParameter(SearchParamType.Uri),
                new SearchModifier(SearchModifierCode.Below),
                input,
                e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateStringExpression(e1, FieldName.Uri, StringOperator.StartsWith, input, false),
                    e2 => ValidateStringExpression(e2, FieldName.Uri, StringOperator.NotStartsWith, "urn:", false)));
        }

        [Theory]
        [InlineData(SearchModifierCode.Exact)]
        [InlineData(SearchModifierCode.Contains)]
        [InlineData(SearchModifierCode.Not)]
        [InlineData(SearchModifierCode.Text)]
        [InlineData(SearchModifierCode.In)]
        [InlineData(SearchModifierCode.NotIn)]
        [InlineData(SearchModifierCode.Type, ResourceType.Patient)]
        public void GivenAUriWithInvalidModifier_WhenBuilding_ThenInvalidSearchOperationExceptionShouldBeThrown(SearchModifierCode modifier, ResourceType? targetTypeModifier = null)
        {
            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(CreateSearchParameter(SearchParamType.Uri), new SearchModifier(modifier, targetTypeModifier?.ToString()), "test"));
        }

        private SearchParameterInfo CreateSearchParameter(SearchParamType searchParameterType)
        {
            return new SearchParameter
            {
                Name = DefaultParamName,
                Code = DefaultParamName,
                Type = searchParameterType,
            }.ToInfo();
        }

        private void Validate(
            SearchParameterInfo searchParameter,
            SearchModifier modifier,
            string value,
            Action<Expression> valueValidator)
        {
            Expression expression = _parser.Parse(searchParameter, modifier, value);
            Assert.NotNull(expression);
            ValidateSearchParameterExpression(expression, DefaultParamName, valueValidator);
        }
    }
}
