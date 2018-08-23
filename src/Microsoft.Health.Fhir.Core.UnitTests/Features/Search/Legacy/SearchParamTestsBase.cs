// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy
{
    public abstract class SearchParamTestsBase
    {
        private static readonly Type DefaultResourceType = typeof(Patient);
        private static readonly string DefaultParamName = "param";
        private static readonly SearchParamType DefaultParamType = SearchParamType.String;
        private static readonly SearchParamValueParser DefaultParser = s => Substitute.For<ISearchValue>();

        protected interface ISearchParamBuilderBase<out TSearchParam>
            where TSearchParam : SearchParam
        {
            Type ResourceType { get; set; }

            string ParamName { get; set; }

            SearchParamValueParser Parser { get; set; }

            TSearchParam ToSearchParam();
        }

        protected abstract ISearchParamBuilderBase<SearchParam> Builder { get; }

        [Fact]
        public void GivenANullResourceType_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Builder.ResourceType = null;

            Assert.Throws<ArgumentNullException>(() => Builder.ToSearchParam());
        }

        [Fact]
        public void GivenAnIncorrectResourceType_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Builder.ResourceType = typeof(int);

            Assert.Throws<ArgumentException>(() => Builder.ToSearchParam());
        }

        [Fact]
        public void GivenANullResource_WhenExtractingValues_ThenExceptionShouldBeThrown()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Assert.Throws<ArgumentNullException>(() => searchParam.ExtractValues(null));
        }

        [Fact]
        public void GivenAnIncorrectResource_WhenExtractingValues_ThenExceptionIsThrown()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Observation observation = new Observation();

            Assert.Throws<ArgumentException>(() => searchParam.ExtractValues(observation));
        }

        [Fact]
        public void GivenASingleValueSelector_WhenExtractingValues_ThenCorrectSearchValuesShouldBeReturned()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Patient patient = new Patient();

            ISearchValue expected = Substitute.For<ISearchValue>();

            ISearchValuesExtractor extractor = Substitute.For<ISearchValuesExtractor>();

            extractor.Extract(patient).Returns(new[] { expected });

            searchParam.AddExtractor(extractor);

            IEnumerable<ISearchValue> actual = searchParam.ExtractValues(patient);

            Assert.NotNull(actual);
            Assert.Single(actual, expected);
        }

        [Fact]
        public void GivenMultipleExtractors_WhenExtractingValues_ThenCorrectSearchValuesShouldBeRetruend()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Patient patient = new Patient();

            ISearchValue[] expected = new[]
            {
                Substitute.For<ISearchValue>(),
                Substitute.For<ISearchValue>(),
                Substitute.For<ISearchValue>(),
            };

            ISearchValuesExtractor extractor1 = Substitute.For<ISearchValuesExtractor>();

            extractor1.Extract(patient).Returns(new[] { expected[0] });

            ISearchValuesExtractor extractor2 = Substitute.For<ISearchValuesExtractor>();

            extractor2.Extract(patient).Returns(new[] { expected[1], expected[2] });

            searchParam.AddExtractor(extractor1);
            searchParam.AddExtractor(extractor2);

            IEnumerable<ISearchValue> actual = searchParam.ExtractValues(patient);

            Assert.NotNull(actual);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void GivenANullEntryFromExtractor_WhenExtractingValues_ThenNullEntryShouldBeFiltered()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Patient patient = new Patient();

            ISearchValuesExtractor extractor = Substitute.For<ISearchValuesExtractor>();

            extractor.Extract(patient).Returns((IEnumerable<ISearchValue>)null);

            searchParam.AddExtractor(extractor);

            IEnumerable<ISearchValue> actual = searchParam.ExtractValues(patient);

            Assert.NotNull(actual);
            Assert.Empty(actual);
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Assert.Throws<ArgumentNullException>(() => searchParam.Parse(null));
        }

        [Fact]
        public void GivenAnEmptyString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            SearchParam searchParam = Builder.ToSearchParam();

            Assert.Throws<ArgumentException>(() => searchParam.Parse(string.Empty));
        }

        [Fact]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned()
        {
            SearchParamValueParser parser = Substitute.For<SearchParamValueParser>();

            ISearchValue searchValue = Substitute.For<ISearchValue>();

            string value = "abc";

            parser(value).Returns(searchValue);

            SearchParam searchParam = new SearchParam(DefaultResourceType, DefaultParamName, DefaultParamType, parser);

            ISearchValue actualValue = searchParam.Parse(value);

            Assert.Equal(searchValue, actualValue);
        }

        protected abstract class SearchParamBuilderBase<TSearchParam>
            : ISearchParamBuilderBase<TSearchParam>
            where TSearchParam : SearchParam
        {
            protected SearchParamBuilderBase()
            {
                ResourceType = DefaultResourceType;
                ParamName = DefaultParamName;
                ParamType = DefaultParamType;
                Parser = DefaultParser;
            }

            public Type ResourceType { get; set; }

            public string ParamName { get; set; }

            public SearchParamType ParamType { get; set; }

            public SearchParamValueParser Parser { get; set; }

            public abstract TSearchParam ToSearchParam();
        }
    }
}
