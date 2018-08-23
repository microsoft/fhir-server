// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;
using static Hl7.Fhir.Model.SearchParameter;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.Expressions.Parsers
{
    public class LegacySearchValueExpressionBuilderTests
    {
        private const string DefaultParamName = "param";

        private static readonly SearchParam DateTimeSearchParam = new SearchParam(
            typeof(Patient),
            DefaultParamName,
            SearchParamType.Date,
            DateTimeSearchValue.Parse);

        private static readonly SearchParam ReferenceSearchParam = new ReferenceSearchParam(
            typeof(Patient),
            DefaultParamName,
            ReferenceSearchValue.Parse,
            new Type[] { typeof(RelatedPerson) });

        private static readonly SearchParam StringSearchParam = new SearchParam(
            typeof(Patient),
            DefaultParamName,
            SearchParamType.String,
            StringSearchValue.Parse);

        private static readonly SearchParam TokenSearchParam = new SearchParam(
            typeof(Patient),
            DefaultParamName,
            SearchParamType.Token,
            TokenSearchValue.Parse);

        private static readonly SearchParam UriSearchParam = new SearchParam(
            typeof(Patient),
            DefaultParamName,
            SearchParamType.Uri,
            UriSearchValue.Parse);

        private readonly ExpressionBuilder _builder = new ExpressionBuilder();

        public static IEnumerable<object[]> GetNonEqualSearchComparatorAsMemberData()
        {
            return GetEnumAsMemberData<SearchComparator>(comparator => comparator != SearchComparator.Eq);
        }

        public static IEnumerable<object[]> GetNoneTokenSearchParamTypeAsMemberData()
        {
            return GetEnumAsMemberData<SearchParamType>(t => t != SearchParamType.Token);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void GivenMissingModifierIsSpecified_WhenBuilt_ThenMissingExpressionShouldBeCreated(string isMissingString, bool expectedIsMissing)
        {
            _builder.Modifier = SearchModifierCode.Missing;
            SetupStringSearchValue(isMissingString);

            Expression expression = _builder.ToExpression();

            ValidateMissingParamExpression(expression, DefaultParamName, expectedIsMissing);
        }

        [Fact]
        public void GivenMissingModifierWithAnInvalidValue_WhenBuilding_ThenExceptionShouldBeThrown()
        {
            _builder.Modifier = SearchModifierCode.Missing;
            SetupStringSearchValue("test");

            Assert.Throws<InvalidSearchOperationException>(
                () => _builder.ToExpression());
        }

        [Fact]
        public void GiveAReference_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string reference = "Patient/123";

            SetupReferenceSearchValue(reference);

            Validate(e => ValidateStringExpression(e, FieldName.Reference, StringOperator.Equals, reference, false));
        }

        [Fact]
        public void GivenAStringWithNoModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string input = "TestString123";

            SetupStringSearchValue(input);

            Validate(e => ValidateStringExpression(e, FieldName.String, StringOperator.StartsWith, input, true));
        }

        [Fact]
        public void GivenAStringWithExact_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string input = "TestString123";

            _builder.Modifier = SearchModifierCode.Exact;
            SetupStringSearchValue(input);

            Validate(e => ValidateStringExpression(e, FieldName.String, StringOperator.Equals, input, false));
        }

        [Fact]
        public void GivenAStringWithContainsModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string input = "TestString123";

            _builder.Modifier = SearchModifierCode.Contains;
            SetupStringSearchValue(input);

            Validate(e => ValidateStringExpression(e, FieldName.String, StringOperator.Contains, input, true));
        }

        [Theory]
        [MemberData(nameof(GetNonEqualSearchComparatorAsMemberData))]
        public void GivenAStringWithInvalidComparator_WhenBuilding_ThenExceptionShouldBeThrown(SearchComparator comparator)
        {
            _builder.Comparator = comparator;
            SetupStringSearchValue("test");

            Assert.Throws<InvalidSearchOperationException>(() => _builder.ToExpression());
        }

        [Theory]
        [InlineData(SearchModifierCode.Above)]
        [InlineData(SearchModifierCode.Below)]
        [InlineData(SearchModifierCode.In)]
        [InlineData(SearchModifierCode.Not)]
        [InlineData(SearchModifierCode.NotIn)]
        [InlineData(SearchModifierCode.Text)]
        [InlineData(SearchModifierCode.Type)]
        public void GivenAStringWithInvalidModifier_WhenBuilding_ThenExceptionShouldBeThrown(SearchModifierCode modifier)
        {
            _builder.Modifier = modifier;
            SetupStringSearchValue("test");

            Assert.Throws<InvalidSearchOperationException>(() => _builder.ToExpression());
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateTimeWithNoComparator_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string input)
        {
            var partialDateTime = PartialDateTime.Parse(input);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            _builder.SearchParam = DateTimeSearchParam;
            _builder.Value = input;

            Validate(e => ValidateMultiaryExpression(
                e,
                MultiaryOperator.And,
                e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, dateTimeSearchValue.Start),
                e2 => ValidateDateTimeBinaryOperatorExpression(e2, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateTimeWithEqComparator_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string input)
        {
            var partialDateTime = PartialDateTime.Parse(input);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            _builder.SearchParam = DateTimeSearchParam;
            _builder.Value = input;
            _builder.Comparator = SearchComparator.Eq;

            Validate(e => ValidateMultiaryExpression(
                e,
                MultiaryOperator.And,
                e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, dateTimeSearchValue.Start),
                e2 => ValidateDateTimeBinaryOperatorExpression(e2, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("2018")]
        [InlineData("2018-02")]
        [InlineData("2018-02-01")]
        [InlineData("2018-02-01T10:00")]
        [InlineData("2018-02-01T10:00-07:00")]
        public void GivenADateTimeWithNeComparator_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string input)
        {
            var partialDateTime = PartialDateTime.Parse(input);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            _builder.SearchParam = DateTimeSearchParam;
            _builder.Value = input;
            _builder.Comparator = SearchComparator.Ne;

            Validate(e => ValidateMultiaryExpression(
                e,
                MultiaryOperator.Or,
                e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.LessThan, dateTimeSearchValue.Start),
                e2 => ValidateDateTimeBinaryOperatorExpression(e2, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, dateTimeSearchValue.End)));
        }

        [Theory]
        [InlineData("2018", SearchComparator.Lt, FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("2018-02", SearchComparator.Lt, FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01", SearchComparator.Lt, FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01T10:00", SearchComparator.Lt, FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Lt, FieldName.DateTimeStart, BinaryOperator.LessThan, true)]
        [InlineData("2018", SearchComparator.Gt, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02", SearchComparator.Gt, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01", SearchComparator.Gt, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01T10:00", SearchComparator.Gt, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Gt, FieldName.DateTimeEnd, BinaryOperator.GreaterThan, false)]
        [InlineData("2018", SearchComparator.Le, FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("2018-02", SearchComparator.Le, FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("2018-02-01", SearchComparator.Le, FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("2018-02-01T10:00", SearchComparator.Le, FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Le, FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual, false)]
        [InlineData("2018", SearchComparator.Ge, FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("2018-02", SearchComparator.Ge, FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("2018-02-01", SearchComparator.Ge, FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("2018-02-01T10:00", SearchComparator.Ge, FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Ge, FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual, true)]
        [InlineData("2018", SearchComparator.Sa, FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02", SearchComparator.Sa, FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01", SearchComparator.Sa, FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01T10:00", SearchComparator.Sa, FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Sa, FieldName.DateTimeStart, BinaryOperator.GreaterThan, false)]
        [InlineData("2018", SearchComparator.Eb, FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("2018-02", SearchComparator.Eb, FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01", SearchComparator.Eb, FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01T10:00", SearchComparator.Eb, FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        [InlineData("2018-02-01T10:00-07:00", SearchComparator.Eb, FieldName.DateTimeEnd, BinaryOperator.LessThan, true)]
        public void GivenADateTimeWithComparatorOfSingleBinaryOperator_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string input, SearchComparator searchComparator, FieldName fieldName, BinaryOperator binaryOperator, bool expectStartTimeValue)
        {
            var partialDateTime = PartialDateTime.Parse(input);
            var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

            _builder.SearchParam = DateTimeSearchParam;
            _builder.Value = input;
            _builder.Comparator = searchComparator;

            Validate(e => ValidateDateTimeBinaryOperatorExpression(e, fieldName, binaryOperator, expectStartTimeValue ? dateTimeSearchValue.Start : dateTimeSearchValue.End));
        }

        [Theory]
        [InlineData("2016", "2015-11-25T12:00:00.0000000+00:00", "2017-02-06T11:59:59.9999999+00:00")]
        [InlineData("2016-02", "2015-11-25T21:36:00.0000000+00:00", "2016-05-07T02:23:59.9999999+00:00")]
        [InlineData("2016-02-01", "2015-11-23T02:24:00.0000000+00:00", "2016-04-11T21:35:59.9999999+00:00")]
        [InlineData("2016-02-01T10:00", "2015-11-23T11:00:06.0000000+00:00", "2016-04-11T09:00:53.9999999+00:00")]
        [InlineData("2016-02-01T10:00-07:00", "2015-11-23T18:42:06.0000000+00:00", "2016-04-11T15:18:53.9999999+00:00")]
        [InlineData("2220", "2240-04-19T09:36:00.0000000+00:00", "2200-09-13T14:23:59.9999999+00:00")]
        [InlineData("2220-02", "2240-04-19T19:12:00.0000000+00:00", "2199-12-12T04:47:59.9999999+00:00")]
        [InlineData("2220-02-01", "2240-04-17T00:00:00.0000000+00:00", "2199-11-16T23:59:59.9999999+00:00")]
        [InlineData("2220-02-01T10:00", "2240-04-17T08:36:06.0000000+00:00", "2199-11-16T11:24:53.9999999+00:00")]
        [InlineData("2220-02-01T10:00-07:00", "2240-04-17T16:18:06.0000000+00:00", "2199-11-16T17:42:53.9999999+00:00")]
        public void GivenADateTimeWithApComparator_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string input, string expectedStartValue, string expectedEndValue)
        {
            using (Mock.Property(() => Clock.UtcNowFunc, () => DateTimeOffset.Parse("2018-01-01T00:00Z")))
            {
                var partialDateTime = PartialDateTime.Parse(input);
                var dateTimeSearchValue = new DateTimeSearchValue(partialDateTime);

                _builder.SearchParam = DateTimeSearchParam;
                _builder.Value = input;
                _builder.Comparator = SearchComparator.Ap;

                Validate(e => ValidateMultiaryExpression(
                    e,
                    MultiaryOperator.And,
                    e1 => ValidateDateTimeBinaryOperatorExpression(e1, FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual, DateTimeOffset.Parse(expectedStartValue)),
                    e2 => ValidateDateTimeBinaryOperatorExpression(e2, FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual, DateTimeOffset.Parse(expectedEndValue))));
            }
        }

        [Fact]
        public void GivenAUrlWithNoModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            string input = "http://example.com/valueset";

            SetupUriSearchValue(input);

            Validate(e => ValidateBinaryExpression(e, FieldName.Uri, BinaryOperator.Equal, input));
        }

        [Fact]
        public void GivenAUrlWithAboveModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            string input = "http://example.com/valueset";

            _builder.Modifier = SearchModifierCode.Above;
            SetupUriSearchValue(input);

            Validate(e => ValidateMultiaryExpression(
                e,
                MultiaryOperator.And,
                e1 => ValidateStringExpression(e1, FieldName.Uri, StringOperator.EndsWith, input, false),
                e2 => ValidateStringExpression(e2, FieldName.Uri, StringOperator.NotStartsWith, "urn:", false)));
        }

        [Fact]
        public void GivenAUrlWithBelowModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            string input = "http://example.com/valueset";

            _builder.Modifier = SearchModifierCode.Below;
            SetupUriSearchValue(input);

            Validate(e => ValidateMultiaryExpression(
                e,
                MultiaryOperator.And,
                e1 => ValidateStringExpression(e1, FieldName.Uri, StringOperator.StartsWith, input, false),
                e2 => ValidateStringExpression(e2, FieldName.Uri, StringOperator.NotStartsWith, "urn:", false)));
        }

        [Fact]
        public void GivenATokenWithTextModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string input = "TestString123";

            _builder.Modifier = SearchModifierCode.Text;
            SetupTokenSearchValue(input);

            Validate(e => ValidateStringExpression(e, FieldName.TokenText, StringOperator.Contains, input, true));
        }

        [Theory]
        [MemberData(nameof(GetNoneTokenSearchParamTypeAsMemberData))]
        public void GivenASearchParamThatDoesNotSupportTextModifier_WhenBuilt_ThenCorrectExpressionShouldBeReturned(SearchParamType paramType)
        {
            _builder.SearchParam = new SearchParam(
                typeof(Patient),
                "test",
                paramType,
                StringSearchValue.Parse);

            _builder.Modifier = SearchModifierCode.Text;
            _builder.Value = "test";

            Assert.Throws<InvalidSearchOperationException>(() => _builder.ToExpression());
        }

        [Fact]
        public void GivenATokenWithNoSystemSpecified_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string code = "code";

            SetupTokenSearchValue(code);

            Validate(e => ValidateStringExpression(e, FieldName.TokenCode, StringOperator.Equals, code, false));
        }

        [Fact]
        public void GivenATokenWithEmptySystemSpecified_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string code = "code";

            SetupTokenSearchValue($"|{code}");

            Validate(
                e =>
                {
                    ValidateMultiaryExpression(
                        e,
                        MultiaryOperator.And,
                        childExpression => ValidateMissingFieldExpression(childExpression, FieldName.TokenSystem),
                        childExpression => ValidateStringExpression(childExpression, FieldName.TokenCode, StringOperator.Equals, code, false));
                });
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenATokenWithEmptyCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeReturned(string code)
        {
            const string system = "system";

            SetupTokenSearchValue($"{system}|{code}");

            Validate(e => ValidateStringExpression(e, FieldName.TokenSystem, StringOperator.Equals, system, false));
        }

        [Fact]
        public void GivenATokenWithSystemAndCodeSpecified_WhenBuilt_ThenCorrectExpressionShouldBeReturned()
        {
            const string system = "system";
            const string code = "code";

            SetupTokenSearchValue($"{system}|{code}");

            Validate(
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
        [InlineData(SearchModifierCode.Not)]
        [InlineData(SearchModifierCode.Type)]
        public void GivenATokenWithInvalidModifier_WhenBuilding_ThenExceptionShouldBeThrown(SearchModifierCode modifier)
        {
            _builder.Modifier = modifier;
            SetupTokenSearchValue("test");

            Assert.Throws<InvalidSearchOperationException>(() => _builder.ToExpression());
        }

        private void SetupReferenceSearchValue(string value)
        {
            _builder.SearchParam = ReferenceSearchParam;
            _builder.Value = value;
        }

        private void SetupStringSearchValue(string value)
        {
            _builder.SearchParam = StringSearchParam;
            _builder.Value = value;
        }

        private void SetupTokenSearchValue(string value)
        {
            _builder.SearchParam = TokenSearchParam;
            _builder.Value = value;
        }

        private void SetupUriSearchValue(string value)
        {
            _builder.SearchParam = UriSearchParam;
            _builder.Value = value;
        }

        private void Validate(params Action<Expression>[] valueValidators)
        {
            Expression expression = _builder.ToExpression();

            Assert.NotNull(expression);

            ValidateParamAndValue(expression, DefaultParamName, valueValidators);
        }

        private class ExpressionBuilder
        {
            private readonly LegacySearchValueExpressionBuilder _builder = new LegacySearchValueExpressionBuilder();

            public ExpressionBuilder()
            {
                Comparator = SearchComparator.Eq;
            }

            public SearchParam SearchParam { get; set; }

            public SearchModifierCode? Modifier { get; set; }

            public SearchComparator Comparator { get; set; }

            public string Value { get; set; }

            public Expression ToExpression()
            {
                return _builder.Build(SearchParam, Modifier, Comparator, Value);
            }
        }
    }
}
