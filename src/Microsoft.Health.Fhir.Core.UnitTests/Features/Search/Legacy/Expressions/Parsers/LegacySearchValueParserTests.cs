// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.SearchParameter;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.Expressions.Parsers
{
    public class LegacySearchValueParserTests
    {
        private static readonly IEnumerable<SearchParamType> SearchParamTypesThatSupportsComparator = new[]
        {
            SearchParamType.Date,
            SearchParamType.Number,
            SearchParamType.Quantity,
        };

        private static readonly IEnumerable<SearchComparator> Prefixes = Enum.GetNames(typeof(SearchComparator))
                .Select(name => (SearchComparator)Enum.Parse(typeof(SearchComparator), name));

        private readonly ISearchParamDefinitionManager _searchParamDefinitionManager = Substitute.For<ISearchParamDefinitionManager>();
        private readonly ILegacySearchValueExpressionBuilder _searchValueExpressionBuilder = Substitute.For<ILegacySearchValueExpressionBuilder>();
        private readonly LegacySearchValueParser _parser;

        public LegacySearchValueParserTests()
        {
            _parser = new LegacySearchValueParser(
                _searchParamDefinitionManager,
                _searchValueExpressionBuilder);
        }

        public static IEnumerable<object[]> GetSerachParamTypeThatSupportsComparatorWithoutEqual()
        {
            return SearchParamTypesThatSupportsComparator.SelectMany(
                type => Prefixes.Where(prefix => prefix != SearchComparator.Eq).Select(prefix => new object[] { type, prefix }));
        }

        public static IEnumerable<object[]> GetSearchParamTypeThatDoesNotSupportComparator()
        {
            yield return new object[] { SearchParamType.Reference };
            yield return new object[] { SearchParamType.String };
            yield return new object[] { SearchParamType.Token };
            yield return new object[] { SearchParamType.Uri };
        }

        public static IEnumerable<object[]> GetSearchParamTypeThatSupportsComparator()
        {
            return SearchParamTypesThatSupportsComparator.SelectMany(
                type => Prefixes.Select(prefix => new object[] { type, prefix }));
        }

        [Fact]
        public void GivenAnInvalidModifier_WhenParsing_ThenExceptionShouldBeThrown()
        {
            SearchParam searchParam = new SearchParam(typeof(Patient), "test", SearchParamType.String, StringSearchValue.Parse);

            Assert.Throws<InvalidSearchOperationException>(
                () => _parser.Parse(searchParam, "invalid", "value"));
        }

        [Theory]
        [InlineData("missing", SearchModifierCode.Missing)]
        [InlineData("exact", SearchModifierCode.Exact)]
        [InlineData("contains", SearchModifierCode.Contains)]
        [InlineData("not", SearchModifierCode.Not)]
        [InlineData("text", SearchModifierCode.Text)]
        [InlineData("in", SearchModifierCode.In)]
        [InlineData("not-in", SearchModifierCode.NotIn)]
        [InlineData("below", SearchModifierCode.Below)]
        [InlineData("above", SearchModifierCode.Above)]
        public void GivenAValidModifier_WhenParsed_ThenCorrectExpressionShouldBeCreated(string modifier, SearchModifierCode expectedModifierCode)
        {
            Type resourceType = typeof(Patient);
            string paramName = "test";
            SearchParamType paramType = SearchParamType.String;
            string value = "value";

            SearchParam searchParam = new SearchParam(resourceType, paramName, paramType, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(paramType);

            Expression expression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                expectedModifierCode,
                SearchComparator.Eq,
                Arg.Is(value))
                .Returns(expression);

            Expression actualExpression = _parser.Parse(searchParam, modifier, value);

            Assert.Equal(expression, actualExpression);
        }

        [Theory]
        [MemberData(nameof(GetSearchParamTypeThatDoesNotSupportComparator))]
        public void GivenASearchParamThatDoesNotSupportComparator_WhenParsed_ThenCorrectExpressionShouldBeCreated(SearchParamType searchParamType)
        {
            Type resourceType = typeof(Patient);
            string paramName = "does-not-support-comparator";
            string value = "ltvalue";

            SearchParam searchParam = new SearchParam(resourceType, paramName, SearchParamType.String, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(searchParamType);

            Expression expression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                null,
                SearchComparator.Eq,
                Arg.Is(value))
                .Returns(expression);

            Expression actualExpression = _parser.Parse(searchParam, null, value);

            Assert.Equal(expression, actualExpression);
        }

        [Theory]
        [MemberData(nameof(GetSearchParamTypeThatDoesNotSupportComparator))]
        public void GivenACompositeSearchParamThatDoesNotSupportComparator_WhenParsed_ThenCorrectExpressionShouldBeCreated(SearchParamType searchParamType)
        {
            Type resourceType = typeof(Patient);
            string paramName = "does-not-support-comparator";

            string system = "system";
            string code = "code";
            string value = "ltvalue";
            string compositeValue = $"{system}|{code}${value}";

            CompositeSearchParam searchParam = new CompositeSearchParam(resourceType, paramName, searchParamType, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(searchParamType);

            Expression expression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                null,
                SearchComparator.Eq,
                Arg.Is(compositeValue))
                .Returns(expression);

            Expression actualExpression = _parser.Parse(searchParam, null, compositeValue);

            Assert.Equal(expression, actualExpression);
        }

        [Theory]
        [MemberData(nameof(GetSearchParamTypeThatSupportsComparator))]
        public void GivenASearchParamThatDoesSupportsComparator_WhenParsed_ThenCorrectExpressionShouldBeCreated(
            SearchParamType searchParamType,
            SearchComparator expectedComparator)
        {
            Type resourceType = typeof(Patient);
            string paramName = "support-comparator";
            string value = "value";
            string valueWithPrefix = $"{expectedComparator.ToString().ToLower()}value";

            SearchParam searchParam = new SearchParam(resourceType, paramName, SearchParamType.String, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(searchParamType);

            Expression expression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                null,
                expectedComparator,
                Arg.Is(value))
                .Returns(expression);

            Expression actualExpression = _parser.Parse(searchParam, null, valueWithPrefix);

            Assert.Equal(expression, actualExpression);
        }

        [Theory]
        [MemberData(nameof(GetSearchParamTypeThatSupportsComparator))]
        public void GivenACompositeSearchParamThatSupportsComparator_WhenParsed_ThenCorrectExpressionShouldBeCreated(
            SearchParamType searchParamType,
            SearchComparator expectedComparator)
        {
            Type resourceType = typeof(Patient);
            string paramName = "support-comparator";

            string system = "system";
            string code = "code";
            string value = "value";
            string inputCompositeValue = $"{system}|{code}${expectedComparator.ToString().ToLowerInvariant()}{value}";
            string compositeValueWithoutComparator = $"{system}|{code}${value}";

            CompositeSearchParam searchParam = new CompositeSearchParam(resourceType, paramName, searchParamType, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(searchParamType);

            Expression expression = Substitute.For<Expression>();

            // By the time the builder is called, the comparator is removed from the string.
            _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                null,
                expectedComparator,
                Arg.Is(compositeValueWithoutComparator))
                .Returns(expression);

            Expression actualExpression = _parser.Parse(searchParam, null, inputCompositeValue);

            Assert.Equal(expression, actualExpression);
        }

        [Theory]
        [MemberData(nameof(GetSerachParamTypeThatSupportsComparatorWithoutEqual))]
        public void GivenASearchParamWithMultipleValuesAndNonEqualPrefixSpecified_WhenParsed_ThenCorrectExpressionShouldBeCreated(
            SearchParamType searchParamType,
            SearchComparator comparator)
        {
            Type resourceType = typeof(Patient);
            string paramName = "param";
            string value = $"{comparator.ToString().ToLowerInvariant()}value1,value2";

            SearchParam searchParam = new SearchParam(resourceType, paramName, SearchParamType.String, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(searchParamType);

            Assert.Throws<InvalidSearchOperationException>(() => _parser.Parse(searchParam, null, value));
        }

        [Fact]
        public void GivenASearchParamWithMultipleValues_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            Type resourceType = typeof(Patient);
            string paramName = "param";

            string value1 = "value1";
            string value2 = "value2";
            string value = $"{value1},{value2}";

            SearchParam searchParam = new SearchParam(resourceType, paramName, SearchParamType.String, StringSearchValue.Parse);

            _searchParamDefinitionManager.GetSearchParamType(resourceType, paramName).Returns(SearchParamType.String);

            Expression expression1 = Substitute.For<Expression>();
            Expression expression2 = Substitute.For<Expression>();

            SetupBuilder(value1, expression1);
            SetupBuilder(value2, expression2);

            Expression actualExpression = _parser.Parse(searchParam, null, value);

            ValidateMultiaryExpression(
                actualExpression,
                MultiaryOperator.Or,
                e => Assert.Equal(expression1, e),
                e => Assert.Equal(expression2, e));

            void SetupBuilder(string stringValue, Expression expression)
            {
                _searchValueExpressionBuilder.Build(
                Arg.Is(searchParam),
                null,
                SearchComparator.Eq,
                Arg.Is(stringValue))
                .Returns(x => expression);
            }
        }
    }
}
