// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
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

            var value = new CompositeSearchValue(
                new[]
                {
                    new ISearchValue[] { new TokenSearchValue(system1, code1, text1) },
                    new ISearchValue[] { new TokenSearchValue(system2, code2, text2) },
                    new ISearchValue[] { new QuantitySearchValue(system3, code3, quantity) },
                });

            var expectedValues = new[]
            {
                CreateTuple("s_0", system1),
                CreateTuple("c_0", code1),
                CreateTuple("s_1", system2),
                CreateTuple("c_1", code2),
                CreateTuple("s_2", system3),
                CreateTuple("c_2", code3),
                CreateTuple("q_2", quantity),
                CreateTuple("lq_2", quantity),
                CreateTuple("hq_2", quantity),
            };

            TestAndValidateOutput(
                "composite",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenACompositeSearchValueWithMultipleValues_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
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
            const string s1 = "test1";
            const string s2 = "test2";
            const string normalizedS1 = "TEST1";
            const string normalizedS2 = "TEST2";

            var value = new CompositeSearchValue(
                new[]
                {
                    new ISearchValue[]
                    {
                        new TokenSearchValue(system1, code1, text1),
                        new TokenSearchValue(system2, code2, text2),
                    },
                    new ISearchValue[] { new QuantitySearchValue(system3, code3, quantity) },
                    new ISearchValue[]
                    {
                        new StringSearchValue(s1),
                        new StringSearchValue(s2),
                    },
                });

            var expectedValues = new[]
            {
                new[]
                {
                    CreateTuple("s_0", system1),
                    CreateTuple("c_0", code1),
                    CreateTuple("s_1", system3),
                    CreateTuple("c_1", code3),
                    CreateTuple("q_1", quantity),
                    CreateTuple("lq_1", quantity),
                    CreateTuple("hq_1", quantity),
                    CreateTuple("n_s_2", normalizedS1),
                },
                new[]
                {
                    CreateTuple("s_0", system1),
                    CreateTuple("c_0", code1),
                    CreateTuple("s_1", system3),
                    CreateTuple("c_1", code3),
                    CreateTuple("q_1", quantity),
                    CreateTuple("lq_1", quantity),
                    CreateTuple("hq_1", quantity),
                    CreateTuple("n_s_2", normalizedS2),
                },
                new[]
                {
                    CreateTuple("s_0", system2),
                    CreateTuple("c_0", code2),
                    CreateTuple("s_1", system3),
                    CreateTuple("c_1", code3),
                    CreateTuple("q_1", quantity),
                    CreateTuple("lq_1", quantity),
                    CreateTuple("hq_1", quantity),
                    CreateTuple("n_s_2", normalizedS1),
                },
                new[]
                {
                    CreateTuple("s_0", system2),
                    CreateTuple("c_0", code2),
                    CreateTuple("s_1", system3),
                    CreateTuple("c_1", code3),
                    CreateTuple("q_1", quantity),
                    CreateTuple("lq_1", quantity),
                    CreateTuple("hq_1", quantity),
                    CreateTuple("n_s_2", normalizedS2),
                },
            };

            TestAndValidateOutput(
                "composite",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenADateTimeSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            var value = new DateTimeSearchValue(PartialDateTime.Parse("2000"), PartialDateTime.Parse("2001"));

            var expectedValues = new[]
            {
                CreateTuple("st", "2000-01-01T00:00:00.0000000+00:00"),
                CreateTuple("et", "2001-12-31T23:59:59.9999999+00:00"),
            };

            TestAndValidateOutput(
                "date",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenANumberSearchValueWithEqualLowAndHighValues_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const decimal number = 1.25m;

            var value = new NumberSearchValue(number);

            TestAndValidateOutput(
                "number",
                value,
                new[] { CreateTuple("n", number), CreateTuple("ln", number), CreateTuple("hn", number) });
        }

        [Fact]
        public void GivenANumberSearchValueWithUnequalLowAndHighValues_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const decimal low = 1.25m;
            const decimal high = 2.25m;

            var value = new NumberSearchValue(low, high);

            TestAndValidateOutput(
                "number",
                value,
                new[] { CreateTuple("ln", low), CreateTuple("hn", high) });
        }

        [Fact]
        public void GivenAQuantitySearchValueWithEqualLowAndHighValues_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const decimal quantity = 3.0m;

            var value = new QuantitySearchValue(
                system,
                code,
                quantity);

            var expectedValues = new[]
            {
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
                CreateTuple("q", quantity),
                CreateTuple("lq", quantity),
                CreateTuple("hq", quantity),
            };

            TestAndValidateOutput(
                "quantity",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenAQuantitySearchValueWithUnequalLowAndHighValues_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const decimal low = 3.0m;
            const decimal high = 5.0m;

            var value = new QuantitySearchValue(
                system,
                code,
                low,
                high);

            var expectedValues = new[]
            {
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
                CreateTuple("lq", low),
                CreateTuple("hq", high),
            };

            TestAndValidateOutput(
                "quantity",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenAReferenceSearchValueWithRelativeReference_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string resourceId = "xyz";

            var value = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, ResourceType.Immunization.ToString(), resourceId);

            var expectedValues = new[]
            {
                CreateTuple(ReferenceResourceTypeName, "Immunization"),
                CreateTuple(ReferenceResourceIdName, resourceId),
            };

            TestAndValidateOutput(
                "reference",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenAReferenceSearchValueWithAbsoluteReference_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            var baseUri = new Uri("https://localhost/stu3/");
            const string resourceId = "123";

            var value = new ReferenceSearchValue(ReferenceKind.Internal, baseUri, ResourceType.Account.ToString(), resourceId);

            var expectedValues = new[]
            {
                CreateTuple("rb", baseUri.ToString()),
                CreateTuple(ReferenceResourceTypeName, "Account"),
                CreateTuple(ReferenceResourceIdName, resourceId),
            };

            TestAndValidateOutput(
                "reference",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenAStringSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string s = "StringWithMixedCase";

            StringSearchValue value = new StringSearchValue(s);

            var expectedValues = new[]
            {
                CreateTuple("s", s),
                CreateTuple("n_s", s.ToUpperInvariant()),
            };

            TestAndValidateOutput(
                "string",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenATokenSearchValueWithNullSystem_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string code = "code";
            const string text = "TEXT";

            var value = new TokenSearchValue(null, code, text);

            var expectedValues = new[]
            {
                CreateTuple(CodeName, code),
                CreateTuple(TextName, text),
            };

            TestAndValidateOutput(
                "token",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenATokenSearchValueWithNullCode_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string text = "TEXT";

            var value = new TokenSearchValue(system, null, text);

            var expectedValues = new[]
            {
                CreateTuple(SystemName, system),
                CreateTuple(TextName, text),
            };

            TestAndValidateOutput(
                "token",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenATokenSearchValueWithNullText_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";

            var value = new TokenSearchValue(system, code, null);

            var expectedValues = new[]
            {
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
            };

            TestAndValidateOutput(
                "token",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenATokenSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string system = "system";
            const string code = "code";
            const string text = "MixedCaseText";

            var value = new TokenSearchValue(system, code, text);

            var expectedValues = new[]
            {
                CreateTuple(SystemName, system),
                CreateTuple(CodeName, code),
                CreateTuple(TextName, text.ToUpperInvariant()),
            };

            TestAndValidateOutput(
                "token",
                value,
                expectedValues);
        }

        [Fact]
        public void GivenAnUriSearchValue_WhenGenerated_ThenCorrectJObjectShouldBeCreated()
        {
            const string uri = "http://uri";

            var value = new UriSearchValue(uri, false);

            TestAndValidateOutput(
                "uri",
                value,
                new[] { CreateTuple("u", uri) });
        }

        private static (string Name, object Value) CreateTuple(string key, object value)
        {
            return (key, value);
        }

        private void TestAndValidateOutput(string parameterName, ISearchValue value, params (string Name, object Value)[][] expectedValues)
        {
            SearchIndexEntry entry = new SearchIndexEntry(new SearchParameterInfo(parameterName, parameterName), value);

            IReadOnlyList<JObject> generatedObjects = _generator.Generate(entry);

            Assert.NotNull(generatedObjects);
            Assert.Collection(
                generatedObjects,
                expectedValues.Select(v => (Action<JObject>)(p => ValidateObject(parameterName, v, p))).ToArray());
        }

        private void ValidateObject(string expectedParameterName, IEnumerable<(string Name, object Value)> expectedValues, JObject obj)
        {
            // Add the parameter name validation.
            expectedValues = expectedValues.Prepend(("p", expectedParameterName));

            Assert.NotNull(obj);
            Assert.Collection(
                obj,
                expectedValues.Select(v => (Action<KeyValuePair<string, JToken>>)(p => ValidateProperty(v.Name, v.Value, p))).ToArray());
        }

        private void ValidateProperty(string expectedName, object expectedValue, KeyValuePair<string, JToken> property)
        {
            Assert.Equal(expectedName, property.Key);

            JValue jValue = Assert.IsType<JValue>(property.Value);

            Assert.Equal(expectedValue, jValue.Value);
        }
    }
}
