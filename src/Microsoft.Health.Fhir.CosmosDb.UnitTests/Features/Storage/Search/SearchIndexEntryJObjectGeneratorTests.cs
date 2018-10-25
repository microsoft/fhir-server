// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    public class SearchIndexEntryJObjectGeneratorTests
    {
        private const string SystemName = "s";
        private const string CodeName = "c";
        private const string TextName = "n_t";
        private const string ReferenceResourceTypeName = "rt";
        private const string ReferenceResourceIdName = "ri";

        private SearchIndexEntryJObjectGenerator _generator = new SearchIndexEntryJObjectGenerator();

        [Fact]
        public void GivenACompositeSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system1 = "s1";
            const string code1 = "s2";
            const string text1 = "t1";
            const string system2 = "s2";
            const string code2 = "c2";
            const string text2 = "T2";
            const decimal quantity = 123.5m;
            const string system3 = "s3";
            const string code3 = "c3";

            var value = new CompositeSearchValue(new ISearchValue[]
            {
                new TokenSearchValue(system1, code1, text1),
                new TokenSearchValue(system2, code2, text2),
                new QuantitySearchValue(system3, code3, quantity),
            });

            TestAndValidateOutput(
                value,
                CreateTuple("s_0", system1),
                CreateTuple("c_0", code1),
                CreateTuple("s_1", system2),
                CreateTuple("c_1", code2),
                CreateTuple("s_2", system3),
                CreateTuple("c_2", code3),
                CreateTuple("q_2", quantity));
        }

        [Fact]
        public void GivenADateTimeSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            var value = new DateTimeSearchValue(
                new PartialDateTime(2000),
                new PartialDateTime(2001));

            TestAndValidateOutput(
                value,
                CreateTuple("st", "2000-01-01T00:00:00.0000000+00:00"),
                CreateTuple("et", "2001-12-31T23:59:59.9999999+00:00"));
        }

        [Fact]
        public void GivenANumberSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const decimal number = 1.25m;

            var value = new NumberSearchValue(number);

            TestAndValidateOutput(
                value,
                CreateTuple("n", number));
        }

        [Fact]
        public void GivenAQuantitySearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const decimal quantity = 3.0m;

            var value = new QuantitySearchValue(
                system,
                code,
                quantity);

            TestAndValidateOutput(
                value,
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
                CreateTuple("q", quantity));
        }

        [Fact]
        public void GivenAReferenceSearchValueWithRelativeReference_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string resourceId = "xyz";

            var value = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, ResourceType.Immunization, resourceId);

            TestAndValidateOutput(
                value,
                CreateTuple(ReferenceResourceTypeName, "Immunization"),
                CreateTuple(ReferenceResourceIdName, resourceId));
        }

        [Fact]
        public void GivenAReferenceSearchValueWithAbsoluteReference_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            var baseUri = new Uri("https://localhost/stu3/");
            const string resourceId = "123";

            var value = new ReferenceSearchValue(ReferenceKind.Internal, baseUri, ResourceType.Account, resourceId);

            TestAndValidateOutput(
                value,
                CreateTuple("rb", baseUri.ToString()),
                CreateTuple(ReferenceResourceTypeName, "Account"),
                CreateTuple(ReferenceResourceIdName, resourceId));
        }

        [Fact]
        public void GivenAStringSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string s = "StringWithMixedCase";

            StringSearchValue value = new StringSearchValue(s);

            TestAndValidateOutput(
                value,
                CreateTuple("s", s),
                CreateTuple("n_s", s.ToUpperInvariant()));
        }

        [Fact]
        public void GivenATokenSearchValueWithNullSystem_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string code = "code";
            const string text = "TEXT";

            var value = new TokenSearchValue(null, code, text);

            TestAndValidateOutput(
                value,
                CreateTuple(CodeName, code),
                CreateTuple(TextName, text));
        }

        [Fact]
        public void GivenATokenSearchValueWithNullCode_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string text = "TEXT";

            var value = new TokenSearchValue(system, null, text);

            TestAndValidateOutput(
                value,
                CreateTuple(SystemName, system),
                CreateTuple(TextName, text));
        }

        [Fact]
        public void GivenATokenSearchValueWithNullText_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";

            var value = new TokenSearchValue(system, code, null);

            TestAndValidateOutput(
                value,
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code));
        }

        [Fact]
        public void GivenATokenSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const string text = "MixedCaseText";

            var value = new TokenSearchValue(system, code, text);

            TestAndValidateOutput(
                value,
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
                CreateTuple(TextName, text.ToUpperInvariant()));
        }

        [Fact]
        public void GivenAnUriSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string uri = "http://uri";

            var value = new UriSearchValue(uri);

            TestAndValidateOutput(
                value,
                CreateTuple("u", uri));
        }

        private static Tuple<string, object> CreateTuple(string key, object value)
        {
            return new Tuple<string, object>(key, value);
        }

        private void TestAndValidateOutput(ISearchValue value, params Tuple<string, object>[] expectedValues)
        {
            value.AcceptVisitor(_generator);

            JObject result = _generator.Output;

            Assert.NotNull(result);
            Assert.Collection(
                result,
                expectedValues.Select(v => (Action<KeyValuePair<string, JToken>>)(p => ValidateProperty(v.Item1, v.Item2, p))).ToArray());
        }

        private void ValidateProperty(string expectedName, object expectedValue, KeyValuePair<string, JToken> property)
        {
            Assert.Equal(expectedName, property.Key);

            JValue jValue = Assert.IsType<JValue>(property.Value);

            Assert.Equal(expectedValue, jValue.Value);
        }
    }
}
