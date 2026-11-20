// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;
using Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class IgnixaRawResourceFactoryTests
    {
        private const string NoMeta = """{"resourceType":"Patient","id":"p"}""";
        private const string WithVersion = """{"resourceType":"Patient","id":"p","meta":{"versionId":"7","_versionId":{"id":"version"}}}""";
        private static readonly string SoftDeleted = $$$"""{"resourceType":"Patient","id":"p","meta":{"extension":[{"url":"{{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}}","valueString":"soft-deleted"}]}}""";

        private static readonly DateTimeOffset PinnedNow = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
#pragma warning disable CS0618 // Match the production import parser.
        private static readonly FhirJsonParser FirelyParser = new(new ParserSettings { PermissiveParsing = true, TruncateDateTimeToDate = true });
#pragma warning restore CS0618

        private readonly IgnixaSchemaContext _schemaContext = new(new VersionSpecificModelInfoProvider());
        private readonly RawResourceFactory _firely = new(new FhirJsonSerializer());
        private readonly IRawResourceFactory _inner = Substitute.For<IRawResourceFactory>();

        private IgnixaRawResourceFactory Factory => new(_inner, _schemaContext, new VersionSpecificModelInfoProvider());

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenKeepMetaMatrix_ThenVersionIdMatchesFirely(bool keepMeta, bool keepVersion)
        {
            RawResource expected = FirelyWrite(WithVersion, keepMeta, keepVersion);

            RawResource actual = Factory.Create(Native(WithVersion), keepMeta, keepVersion);

            Assert.Equal(Version(expected.Data), Version(actual.Data));
            Assert.Equal(keepMeta, actual.IsMetaSet);
            Assert.Equal(FhirResourceFormat.Json, actual.Format);
            AssertExactly(expected.Data, actual.Data);
        }

        [Fact]
        public void GivenNoMeta_WhenKeepMetaFalse_ThenNoMetaProperty()
        {
            Assert.DoesNotContain("\"meta\"", Factory.Create(Native(NoMeta), false).Data);
        }

        [Fact]
        public void GivenNoMeta_WhenKeepMetaTrueAndNotKeepVersion_ThenMetaHasVersionOne()
        {
            Assert.Contains("\"meta\":{\"versionId\":\"1\"}", Factory.Create(Native(NoMeta), true).Data);
        }

        [Fact]
        public void GivenContainedBeforeMeta_ThenRootMetaIsFirst()
        {
            const string json = """{"resourceType":"Patient","contained":[{"resourceType":"Organization","id":"o","meta":{"versionId":"9","lastUpdated":"2019-01-01T00:00:00Z"}}],"id":"p","meta":{"versionId":"2","lastUpdated":"2020-01-01T00:00:00Z"}}""";

            string data = Factory.Create(Native(json), true, true).Data;

            Assert.StartsWith("""{"resourceType":"Patient","id":"p","meta":{""", data);
            Assert.Equal("2", Version(data));
            Assert.Contains("\"lastUpdated\":\"2020-01-01T00:00:00", data);
            AssertExactly(FirelyWrite(json).Data, data);
        }

        [Theory]
        [InlineData("2020-01-01", "2020-01-01T00:00:00+00:00")]
        [InlineData("2020-01-01T00:00:00.100+00:00", "2020-01-01T00:00:00.1+00:00")]
        [InlineData("2020-01-01T00:00:00.000Z", "2020-01-01T00:00:00+00:00")]
        public void GivenTrailingZeroMilliseconds_ThenLastUpdatedIsInstantString(string instant, string expected)
        {
            string json = $$$"""{"resourceType":"Patient","meta":{"lastUpdated":"{{{instant}}}"}}""";

            string data = Factory.Create(Native(json), true, true).Data;

            Assert.Contains($"\"lastUpdated\":\"{expected}\"", data);
            Assert.DoesNotContain("\\u002B", data);
            AssertExactly(FirelyWrite(json).Data, data);
        }

        [Theory]
        [InlineData("\"01/02/2020\"")]
        [InlineData("\"2020-01-01T00:00:00\"")]
        [InlineData("\"2020-01-01T00:00:00+00:60\"")]
        [InlineData("123")]
        [InlineData("{\"value\":\"2020-01-01\"}")]
        [InlineData("null")]
        public void GivenInvalidLastUpdated_ThenRejectsWithPath(string token)
        {
            string json = $$$"""{"resourceType":"Patient","meta":{"lastUpdated":{{{token}}}}}""";

            var error = Assert.Throws<FormatException>(() => Factory.Create(Native(json), false));

            Assert.Contains("Patient.meta.lastUpdated", error.Message);
        }

        [Theory]
        [InlineData("""{"resourceType":"Patient","active":"true"}""", """{"resourceType":"Patient","active":true}""")]
        [InlineData("""{"resourceType":"Patient","multipleBirthInteger":"2"}""", """{"resourceType":"Patient","multipleBirthInteger":2}""")]
        [InlineData("""{"resourceType":"Observation","valueQuantity":{"value":"1.50"}}""", """{"resourceType":"Observation","valueQuantity":{"value":1.50}}""")]
        [InlineData("""{"resourceType":"Patient","name":[{"family":123,"given":[true]}]}""", """{"resourceType":"Patient","name":[{"family":"123","given":["true"]}]}""")]
        public void GivenAlternatePrimitiveToken_ThenWritesSchemaLiteral(string input, string expected)
        {
            Assert.Equal(expected, FirelyWrite(input, false).Data);
            Assert.Equal(expected, Factory.Create(Native(input), false).Data);
        }

        [Theory]
        [InlineData("active", "\"yes\"")]
        [InlineData("multipleBirthInteger", "\"2.5\"")]
        [InlineData("multipleBirthInteger", "true")]
        [InlineData("multipleBirthInteger", "\"2147483648\"")]
        [InlineData("active", "[true]")]
        [InlineData("maritalStatus", "\"single\"")]
        [InlineData("_active", "\"bad\"")]
        [InlineData("name", "[{\"given\":[{\"value\":\"bad\"}]}]")]
        [InlineData("name", "[{\"given\":[{\"extension\":[{\"unknown\":null}]}]}]")]
        [InlineData("contained", "[{\"resourceType\":\"HumanName\",\"family\":\"Smith\"}]")]
        [InlineData("text", "{\"status\":\"generated\",\"div\":123}")]
        public void GivenInvalidPrimitiveToken_ThenRejectsWithPath(string name, string token)
        {
            string json = $$$"""{"resourceType":"Patient","{{{name}}}":{{{token}}}}""";

            var error = Assert.Throws<FormatException>(() => Factory.Create(Native(json), false));

            Assert.Contains($"Patient.{name}", error.Message);
        }

#if R4 || R4B
        [Fact]
        public void GivenExampleScenarioInstance_ThenResourceTypeIsAPropertyNotADiscriminator()
        {
            const string json = """{"resourceType":"ExampleScenario","status":"draft","instance":[{"resourceId":"one","resourceType":"Patient","name":"Example patient"}]}""";

            string data = Factory.Create(Native(json), false).Data;

            using var document = JsonDocument.Parse(data);
            JsonElement instance = document.RootElement.GetProperty("instance")[0];
            Assert.Equal("Patient", instance.GetProperty("resourceType").GetString());
            Assert.Equal("Example patient", instance.GetProperty("name").GetString());
            AssertExactly(FirelyWrite(json, false).Data, data);
        }
#endif

        [Theory]
        [InlineData("<div xmlns=\"http://www.w3.org/1999/xhtml\"><b>\"é\" &amp;</b></div>", true)]
        [InlineData("a\u00a0b", false)]
        [InlineData("a\u0085\u2028\u2029b", false)]
        [InlineData("a\U0001f600b", false)]
        [InlineData("a\tb", false)]
        [InlineData("a\b\f\n\r\u0001\u001b\\\"b", false)]
        public void GivenMarkupAndNonAscii_ThenEscapingMatchesFirely(string value, bool narrative)
        {
            string literal = JsonSerializer.Serialize(value);
            string json = narrative
                ? $$$"""{"resourceType":"Patient","text":{"status":"generated","div":{{{literal}}}}}"""
                : $$$"""{"resourceType":"Observation","code":{"text":{{{literal}}}}}""";

            string data = Factory.Create(Native(json), false).Data;

            Assert.Equal(FirelyWrite(json, false).Data, data);
        }

        [Fact]
        public void GivenLoneObjectForCollection_ThenWrittenAsArray()
        {
            const string json = """{"resourceType":"Patient","id":"patient-1","generalPractitioner":{"reference":"Practitioner/1"}}""";

            string data = Factory.Create(Native(json), false).Data;

            Assert.Contains("\"generalPractitioner\":[{", data);
            AssertExactly(FirelyWrite(json, false).Data, data);
        }

        [Fact]
        public void GivenDecimalAndTemporalPrecision_ThenLexicalFormPreserved()
        {
            const string json = """{"resourceType":"Observation","code":{"text":"  a  b  "},"valueQuantity":{"value":1.50},"effectiveDateTime":"2020-01-01T10:00:00.000+05:00","contained":[{"resourceType":"Patient","birthDate":"1970"}]}""";

            string data = Factory.Create(Native(json), false).Data;

            Assert.Contains("\"value\":1.50", data);
            Assert.Contains("\"birthDate\":\"1970\"", data);
            Assert.Contains("\"effectiveDateTime\":\"2020-01-01T10:00:00.000+05:00\"", data);
            Assert.Contains("\"text\":\"  a  b  \"", data);
            var expected = JsonNode.Parse(FirelyWrite(json, false).Data);
            Assert.Equal("a  b", expected["code"]["text"].GetValue<string>());
            expected["code"]["text"] = "  a  b  ";
            AssertExactly(expected.ToJsonString(), data);
        }

        [Fact]
        public void GivenExtensionOnlyPrimitive_ThenCompanionPreserved()
        {
            const string extension = """{"extension":[{"url":"http://example.org/note","valueString":"shadow"}]}""";
            string json = $$$"""{"resourceType":"Patient","_birthDate":{{{extension}}},"name":[{"given":["a",null],"_given":[null,{{{extension}}}]}]}""";

            string data = Factory.Create(Native(json), false).Data;

            Assert.DoesNotContain("\"birthDate\"", data);
            Assert.Contains($"\"_birthDate\":{extension}", data);
            Assert.Contains($"\"given\":[\"a\",null],\"_given\":[null,{extension}]", data);
            AssertExactly(FirelyWrite(json, false).Data, data);
        }

        [Fact]
        public void GivenLoneComplexChoicesAndRecursiveBackbones_ThenNestedCollectionsUseSchema()
        {
            const string json = """{"resourceType":"Bundle","entry":{"resource":{"resourceType":"Questionnaire","item":{"linkId":"p","type":"group","item":{"linkId":"c","type":"string","extension":{"url":"http://example.org/name","valueHumanName":{"given":"Ada","_given":{"id":"g"}}}}}}}}""";

            string data = Factory.Create(Native(json), false).Data;

            Assert.Contains("\"entry\":[{", data);
            Assert.Contains("\"item\":[{\"linkId\":\"p\"", data);
            Assert.Contains("\"item\":[{\"linkId\":\"c\"", data);
            Assert.Contains("\"extension\":[{", data);
            Assert.Contains("\"valueHumanName\":{\"given\":[\"Ada\"],\"_given\":[{\"id\":\"g\"}]}", data);
            AssertExactly(FirelyWrite(json, false).Data, data);
        }

        [Fact]
        public void GivenEmptiedMetaExtension_ThenNoEmptyArray()
        {
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string data = ImportWrite(SoftDeleted, ImportMode.IncrementalLoad, native: true).Data;

                Assert.DoesNotContain("\"extension\":[]", data);
                Assert.DoesNotContain(KnownFhirPaths.AzureSoftDeletedExtensionUrl, data);
                AssertExactly(ImportWrite(SoftDeleted, ImportMode.IncrementalLoad, native: false).Data, data);
            }
        }

        [Fact]
        public void GivenKeepMetaFalse_ThenCarriedNodeIsUnchanged()
        {
            ResourceElement element = Native(WithVersion);
            var carrier = (ResourceJsonNode)element.ResourceInstance;
            string before = carrier.ToSourceNavigator().Meta<JsonNode>().ToJsonString();

            string first = Factory.Create(element, false).Data;
            string second = Factory.Create(element, false).Data;

            Assert.Equal(before, carrier.ToSourceNavigator().Meta<JsonNode>().ToJsonString());
            Assert.Equal(first, second);
            Assert.DoesNotContain("\"_versionId\"", first);
        }

        [Fact]
        public void GivenNonNativeCarrier_ThenDelegatesToInnerFactory()
        {
            ResourceElement element = FirelyParser.Parse<Resource>(NoMeta).ToResourceElement();
            var expected = new RawResource(NoMeta, FhirResourceFormat.Json, false);
            _inner.Create(element, false, true).Returns(expected);

            RawResource actual = Factory.Create(element, false, true);

            Assert.Same(expected, actual);
            _inner.Received(1).Create(element, false, true);
            Assert.Single(_inner.ReceivedCalls());
        }

        [Fact]
        public void GivenNativeCarrier_ThenInnerFactoryNeverCalled()
        {
            Assert.Equal(NoMeta, Factory.Create(Native(NoMeta), false).Data);
            Assert.Empty(_inner.ReceivedCalls());
        }

        [Fact]
        public void GivenNativeWriteThrows_ThenExceptionPropagates()
        {
            ResourceElement element = Native("""{"resourceType":"Observation","valueQuantity":{"value":1.50}}""");
            var carrier = (ResourceJsonNode)element.ResourceInstance;
            carrier.ToSourceNavigator().Meta<JsonNode>()["valueQuantity"]["value"] = double.NaN;

            Assert.Throws<ArgumentException>(() => Factory.Create(element, false));
            Assert.Empty(_inner.ReceivedCalls());
        }

        [Fact]
        public void GivenLoneSurrogateInString_WhenWriting_ThenFailurePropagates()
        {
            ResourceElement element = Native("""{"resourceType":"Patient","id":"p"}""");
            var carrier = (ResourceJsonNode)element.ResourceInstance;
            carrier.ToSourceNavigator().Meta<JsonNode>()["id"] = "a\ud800b";

            Assert.Throws<FormatException>(() => Factory.Create(element, false));
            Assert.Empty(_inner.ReceivedCalls());
        }

        [Fact]
        public void GivenLastUpdatedCompanion_WhenWritingDirectly_ThenPreservesIt()
        {
            const string json = """{"resourceType":"Patient","meta":{"lastUpdated":"2020-01-01T00:00:00Z","_lastUpdated":{"id":"original"}}}""";

            string data = Factory.Create(Native(json), false).Data;

            AssertExactly(FirelyWrite(json, false).Data, data);
            Assert.Contains("\"_lastUpdated\":{\"id\":\"original\"}", data);
        }

        [Theory]
        [InlineData(ImportMode.InitialLoad)]
        [InlineData(ImportMode.IncrementalLoad)]
        public void GivenLastUpdatedCompanion_WhenImportWrites_ThenReplacementDropsOnlyRootCompanion(ImportMode mode)
        {
            const string json = """{"resourceType":"Patient","id":"p","meta":{"lastUpdated":"2020-01-01T00:00:00Z","_lastUpdated":{"id":"original"}},"contained":[{"resourceType":"Patient","id":"c","meta":{"lastUpdated":"2019-01-01T00:00:00Z","_lastUpdated":{"id":"contained"}}}]}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string expected = ImportWrite(json, mode, native: false).Data;
                var meta = JsonNode.Parse(expected)["meta"].AsObject();
                Assert.False(meta.ContainsKey("_lastUpdated"));
                Assert.Contains("\"_lastUpdated\":{\"id\":\"contained\"}", expected);

                AssertExactly(expected, ImportWrite(json, mode, native: true).Data);
            }
        }

        [Fact]
        public void GivenDateTypedElementWithDateTime_ThenStoredAsDate()
        {
            const string json = """{"resourceType":"Patient","id":"p","birthDate":"2001-02-03T04:05:06Z"}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string data = ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data;

                Assert.Contains("\"birthDate\":\"2001-02-03\"", data);
                AssertExactly(ImportWrite(json, ImportMode.IncrementalLoad, native: false).Data, data);
            }
        }

        [Theory]
        [MemberData(nameof(ImportResourceParserParityTests.AcceptedParityCorpus), MemberType = typeof(ImportResourceParserParityTests))]
        public void GivenCorpus_ThenExactlyEqualToFirely(string json, ImportMode mode)
        {
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string expected = ImportWrite(json, mode, native: false).Data;
                string actual = ImportWrite(json, mode, native: true).Data;

                AssertExactly(expected, actual);
            }
        }

        [Theory]
        [InlineData("{", ImportMode.IncrementalLoad, typeof(FormatException))]
        [InlineData("""{"resourceType":"Patient","id":"bad id"}""", ImportMode.IncrementalLoad, typeof(BadRequestException))]
        [InlineData("""{"resourceType":"Patient","id":"p","generalPractitioner":{"reference":"Practitioner?identifier=x"}}""", ImportMode.InitialLoad, typeof(NotSupportedException))]
        [InlineData("""{"resourceType":"Patient","id":"p","foo":"unknown"}""", ImportMode.IncrementalLoad, typeof(FormatException), "Patient.foo", typeof(StructuralTypeException))]
        [InlineData("""{"resourceType":"Patient","id":"p","maritalStatus":{"resourceType":"Patient"}}""", ImportMode.IncrementalLoad, typeof(FormatException), "Patient.maritalStatus.resourceType", typeof(StructuralTypeException))]
        [InlineData("""{"resourceType":"Patient","id":"p","deceasedDateTime":{"resourceType":"Patient"}}""", ImportMode.IncrementalLoad, typeof(FormatException), "Patient.deceasedDateTime", typeof(StructuralTypeException))]
        [InlineData("""{"resourceType":"Patient","id":"p","birthDate":{"resourceType":"Patient"}}""", ImportMode.IncrementalLoad, typeof(FormatException), "Patient.birthDate", typeof(StructuralTypeException))]
        public void GivenRejectedCorpus_ThenBothProvidersThrowExpectedType(string json, ImportMode mode, Type exceptionType, string nativePath = null, Type firelyExceptionType = null)
        {
            Exception nativeError = Record.Exception(() => ImportWrite(json, mode, native: true));
            Assert.Throws(firelyExceptionType ?? exceptionType, () => ImportWrite(json, mode, native: false));
            Assert.IsType(exceptionType, nativeError);
            if (nativePath is not null)
            {
                Assert.Contains(nativePath, nativeError.Message);
            }
        }

        [Theory]
        [InlineData(""" "birthDate":{"id":"inline","extension":[{"url":"inline"}]} """, """ "_birthDate":{"id":"inline","extension":[{"url":"inline"}]} """)]
        [InlineData(""" "birthDate":{"id":"inline"},"_birthDate":{"id":"shadow","extension":[{"url":"shadow"}]} """, """ "_birthDate":{"id":"inline"} """, true)]
        [InlineData(""" "_birthDate":{"id":"shadow","extension":[{"url":"shadow"}]},"birthDate":{"extension":[{"url":"first"},{"url":"second"}]} """, """ "_birthDate":{"extension":[{"url":"first"},{"url":"second"}]} """, true)]
        [InlineData(""" "birthDate":{},"_birthDate":{"id":"shadow"} """, "", false, """ "_birthDate":{"id":"shadow"} """)]
        [InlineData(""" "name":[{"given":[{"id":"inline","extension":[{"url":"inline"}]},"b",null,{"id":"last"}],"_given":[{"id":"shadow","extension":[{"url":"shadow"}]},{"id":"middle"},{"id":"nullSlot"},null,{"id":"extra"}]}] """, """ "name":[{"given":[null,"b",null,null,null],"_given":[{"id":"inline","extension":[{"url":"inline"}]},{"id":"middle"},{"id":"nullSlot"},{"id":"last"},{"id":"extra"}]}] """, true)]
        [InlineData(""" "name":[{"given":[null,{"id":"inline"},null]}] """, """ "name":[{"_given":[{"id":"inline"}]}] """)]
        [InlineData(""" "name":[{"given":[null,{"id":"inline"},"b",null]}] """, """ "name":[{"given":[null,"b"],"_given":[{"id":"inline"},null]}] """)]
        [InlineData(""" "name":[{"given":["a",null,"b",null],"_given":[null,null,{"id":"shadow"}]}] """, """ "name":[{"given":["a","b"],"_given":[null,{"id":"shadow"}]}] """)]
        [InlineData(""" "name":[{"_given":[null,{"id":"shadow"}],"given":["a",{"id":"inline"}]}] """, """ "name":[{"given":["a",null],"_given":[null,{"id":"inline"}]}] """, true)]
        [InlineData(""" "name":[{"given":{"id":"inline"},"_given":[null,{"id":"extra"}]}] """, """ "name":[{"_given":[{"id":"inline"},{"id":"extra"}]}] """)]
        [InlineData(""" "name":[{"given":[{}]}] """, "")]
        [InlineData(""" "contained":[{"resourceType":"Patient","id":"c","meta":{"lastUpdated":{"id":"inline"}}}] """, """ "contained":[{"resourceType":"Patient","id":"c","meta":{"_lastUpdated":{"id":"inline"}}}] """)]
        [InlineData(""" "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">ok</div>","_div":{"id":"shadow","extension":[{"url":"shadow"}]}} """, """ "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">ok</div>","_div":{"id":"shadow","extension":[{"url":"shadow"}]}} """)]
        [InlineData(""" "text":{"status":"generated","div":{"id":"inline"},"_div":{"id":"shadow"}} """, """ "text":{"status":"generated","_div":{"id":"inline"}} """, true)]
        [InlineData(""" "text":{"status":"generated","_div":{"id":"shadow"}} """, """ "text":{"status":"generated","_div":{"id":"shadow"}} """)]
        public void GivenPrimitiveMetadata_ThenNativeMatchesIgnixaParserWithFirelyWriter(string input, string persisted, bool firelyConflict = false, string firelyPersisted = null)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p",{{{input}}}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string metadata = """ "meta":{"versionId":"1","lastUpdated":"2026-06-01T00:00:00+00:00"} """;
                string Expected(string fields) => $$$"""{"resourceType":"Patient","id":"p",{{{metadata}}}{{{(fields.Length == 0 ? string.Empty : "," + fields)}}}}""";
                string expected = Expected(persisted);
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data;
                AssertExactly(expected, mixed);
                if (firelyConflict)
                {
                    Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: false));
                }
                else
                {
                    AssertExactly(Expected(firelyPersisted ?? persisted), ImportWrite(json, ImportMode.IncrementalLoad, native: false).Data);
                }

                AssertExactly(expected, ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data);
            }
        }

        [Fact]
        public void GivenInlineAndExplicitPrimitiveMetadata_WhenWritten_ThenCarrierIsUnchanged()
        {
            const string json = """{"resourceType":"Patient","name":[{"given":["a",{"id":"inline"}],"_given":[null,{"id":"shadow"}]}]}""";
            ResourceElement element = Native(json);
            var carrier = (ResourceJsonNode)element.ResourceInstance;
            JsonNode node = carrier.ToSourceNavigator().Meta<JsonNode>();
            string before = node.ToJsonString();

            string first = Factory.Create(element, false).Data;
            string second = Factory.Create(element, false).Data;

            Assert.Equal("""{"resourceType":"Patient","name":[{"given":["a",null],"_given":[null,{"id":"inline"}]}]}""", first);
            Assert.Equal(first, second);
            Assert.Equal(before, node.ToJsonString());
        }

        [Fact]
        public void GivenUnrelatedPrimitiveObject_ThenNativeRejectsInsteadOfDroppingIt()
        {
            const string json = """{"resourceType":"Patient","id":"p","birthDate":{"resourceType":"Patient"}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                Assert.DoesNotContain("birthDate", ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data);
                Assert.Contains("Patient.birthDate", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
            }
        }

        [Theory]
        [InlineData("  ")]
        [InlineData("\u00a0")]
        [InlineData("")]
        public void GivenBlankString_ThenNativeKeepsLiteralAndFirelyPathsDiffer(string literal)
        {
            string token = JsonSerializer.Serialize(literal);
            string json = $$$"""{"resourceType":"Patient","id":"p","name":[{"family":{{{token}}}}]}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data;
                Assert.Equal(string.Empty, JsonNode.Parse(mixed)["name"][0]["family"].GetValue<string>());
                Assert.DoesNotContain("\"family\"", ImportWrite(json, ImportMode.IncrementalLoad, native: false).Data);

                string native = ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data;
                Assert.Contains($"\"family\":\"{literal}\"", native);
            }
        }

        [Theory]
        [InlineData("  ")]
        [InlineData("\u00a0")]
        [InlineData("bogus")]
        public void GivenInvalidGender_ThenBothIgnixaImportPathsReject(string literal)
        {
            string token = JsonSerializer.Serialize(literal);
            string json = $$$"""{"resourceType":"Patient","id":"p","gender":{{{token}}}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                Assert.Contains("AdministrativeGender", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true)).Message);
                Assert.Contains("Patient.gender", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
            }
        }

        [Theory]
        [InlineData(""" "gender":" male " """, """ {"gender":"male"} """)]
        [InlineData(""" "photo":[{"contentType":""}] """, """ {"photo":[{"contentType":""}]} """)]
        [InlineData(""" "maritalStatus":{"coding":[{"code":"  "}]} """, """ {"maritalStatus":{"coding":[{"code":""}]}} """)]
        [InlineData(""" "birthDate":{"id":"inline"},"_birthDate":{"foo":"ignored"} """, """ {"_birthDate":{"id":"inline"}} """)]
        [InlineData(""" "_birthDate":"2001-02-03" """, """ {"birthDate":"2001-02-03"} """)]
        [InlineData(""" "_birthDate":123 """, """ {"birthDate":"123"} """)]
        [InlineData(""" "name":[{"given":[{"id":"inline"},"b"],"_given":[{"foo":"ignored"},null]}] """, """ {"name":[{"given":[null,"b"],"_given":[{"id":"inline"},null]}]} """)]
        [InlineData(""" "name":[{"given":[{"extension":[{"valueString":{},"_valueString":{"id":"x"}}]},"b",{"id":"c"}]}] """, """ {"name":[{"given":["b",null],"_given":[null,{"id":"c"}]}]} """)]
        [InlineData(""" "contained":[{"resourceType":"Patient","id":"c","meta":{"lastUpdated":null,"_lastUpdated":{"id":"companion"}}}] """, """ {"contained":[{"resourceType":"Patient","id":"c","meta":{"_lastUpdated":{"id":"companion"}}}]} """)]
        public void GivenPrimitiveBoundaryInput_ThenBothIgnixaWritersMatchExpectedFields(string input, string fields)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p",{{{input}}}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data;
                string native = ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data;
                var expected = JsonNode.Parse(fields).AsObject();
                var mixedFields = JsonNode.Parse(mixed).AsObject();
                var nativeFields = JsonNode.Parse(native).AsObject();
                foreach (string common in new[] { "resourceType", "id", "meta" })
                {
                    mixedFields.Remove(common);
                    nativeFields.Remove(common);
                }

                Assert.True(JsonNode.DeepEquals(expected, mixedFields), mixed);
                Assert.True(JsonNode.DeepEquals(expected, nativeFields), native);
            }
        }

        [Theory]
        [InlineData(""" "telecom":[{"system":"  "}] """, "ContactPointSystem", "Patient.telecom[0].system")]
        [InlineData(""" "contact":[{"gender":"bogus"}] """, "AdministrativeGender", "Patient.contact[0].gender")]
        public void GivenInvalidNestedCode_ThenBothIgnixaImportPathsReject(string input, string enumName, string path)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p",{{{input}}}}""";
            Assert.Contains(enumName, Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true)).Message);
            Assert.Contains(path, Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
        }

        [Theory]
        [InlineData("aGVsbG8=")]
        [InlineData(" a G\tV\r\ns bG8=\t")]
        public void GivenBase64Binary_ThenBothIgnixaWritersEmitUnbrokenBase64(string literal)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p","photo":[{"data":{{{JsonSerializer.Serialize(literal)}}},"title":"a  b"}]}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data;
                string native = ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data;

                Assert.Contains("\"data\":\"aGVsbG8=\"", mixed);
                Assert.Contains("\"title\":\"a  b\"", mixed);
                AssertExactly(mixed, native);
            }
        }

        [Theory]
        [InlineData("\"not*base64\"")]
        [InlineData("\"abc\"")]
        [InlineData("\"aGVs\\u00a0bG8=\"")]
        [InlineData("\"aGVs\\fbG8=\"")]
        [InlineData("\"aGVs\\u000bbG8=\"")]
        [InlineData("\"aGVs\\u2028bG8=\"")]
        [InlineData("\"-w==\"")]
        [InlineData("\"aGVsbG8==\"")]
        [InlineData("\"aGVsbG9=\"")]
        [InlineData("123")]
        public void GivenInvalidBase64_ThenBothWritersReject(string token)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p","photo":[{"data":{{{token}}}}]}""";
            Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, _firely, true));
            Assert.Contains("Patient.photo[0].data", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
        }

        [Theory]
        [InlineData("\"+1\"", "1")]
        [InlineData("\"01\"", "1")]
        [InlineData("\".5\"", "0.5")]
        [InlineData("\" 1.50 \"", "1.50")]
        [InlineData("1e2", "1e2")]
        [InlineData("1.0E+2", "1.0E+2")]
        [InlineData("1.50", "1.50")]
        public void GivenDecimal_ThenNativePreservesJsonSpellingOrCanonicalizes(string token, string expected)
        {
            string json = $$$"""{"resourceType":"Observation","id":"p","valueQuantity":{"value":{{{token}}}}}""";
            using var mixed = JsonDocument.Parse(ImportWrite(json, ImportMode.IncrementalLoad, _firely, true).Data);
            using var native = JsonDocument.Parse(ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data);
            var actual = native.RootElement.GetProperty("valueQuantity").GetProperty("value");
            Assert.Equal(expected, actual.GetRawText());
            Assert.Equal(mixed.RootElement.GetProperty("valueQuantity").GetProperty("value").GetDecimal(), actual.GetDecimal());
        }

        [Theory]
        [InlineData(""" "name":[{"given":["b",{"id":"c"}],"_given":["discard","c-value"]}] """, """ "name":[{"given":["b","c-value"],"_given":[null,{"id":"c"}]}] """)]
        [InlineData(""" "name":[{"_given":"a"}] """, """ "name":[{"given":["a"]}] """)]
        [InlineData(""" "telecom":[{"system":{"id":"inline"},"_system":"phone"}] """, """ "telecom":[{"system":"phone","_system":{"id":"inline"}}] """)]
        [InlineData(""" "gender":"male","_gender":"female" """, """ "gender":"male" """)]
        [InlineData(""" "contained":[{"resourceType":"Patient","id":"c","meta":{"lastUpdated":null,"_lastUpdated":{"id":"i"}}}] """, """ "contained":[{"resourceType":"Patient","id":"c","meta":{"_lastUpdated":{"id":"i"}}}] """)]
        public void GivenPermissiveCompanion_ThenNativeMatchesFirely(string input, string expected)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p",{{{input}}}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, true).Data;
                string native = ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data;
                Assert.True(mixed.Contains(expected.Trim(), StringComparison.Ordinal), $"IF={mixed}; II={native}");
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(mixed), JsonNode.Parse(native)), $"IF={mixed}; II={native}");
            }
        }

        [Fact]
        public void GivenNullCollectionSlotWithScalarCompanion_ThenNativePromotesTheValue()
        {
            const string json = """{"resourceType":"Patient","id":"p","name":[{"given":[null,"b"],"_given":["a","discard"]}]}""";
            Assert.Contains("\"given\":[\"b\"]", ImportWrite(json, ImportMode.IncrementalLoad, _firely, true).Data);
            Assert.Contains("\"given\":[\"a\",\"b\"]", ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data);
        }

        [Fact]
        public void GivenPaddedNonTextPrimitives_ThenNativeTrimsBeforeWriting()
        {
            const string json = """{"resourceType":"Patient","id":"p","active":" true ","language":" en ","multipleBirthInteger":" 2 ","identifier":[{"system":" http://example.org ","value":"  text  "}],"meta":{"lastUpdated":" 2020-01-01T00:00:00Z "}}""";
            string native = Factory.Create(Native(json), true, true).Data;
            Assert.Contains("\"lastUpdated\":\"2020-01-01T00:00:00+00:00\"", native);
            Assert.Contains("\"active\":true", native);
            Assert.Contains("\"language\":\"en\"", native);
            Assert.Contains("\"multipleBirthInteger\":2", native);
            Assert.Contains("\"system\":\"http://example.org\"", native);
            Assert.Contains("\"value\":\"  text  \"", native);
        }

        [Fact]
        public void GivenOutOfRangeDecimal_ThenBothWritersReject()
        {
            const string json = """{"resourceType":"Observation","id":"p","valueQuantity":{"value":123456789012345678901234567890}}""";
            Assert.Throws<OverflowException>(() => ImportWrite(json, ImportMode.IncrementalLoad, _firely, true));
            Assert.Contains("Observation.valueQuantity.value", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
        }

        [Theory]
        [InlineData("Observation", """ "valueCode":"bogus" """, """ "valueCode":"bogus" """)]
        [InlineData("Patient", """ "active":[true] """, """ "active":true """)]
        public void GivenMalformedShapeAcceptedByFirely_ThenNativeRejects(string resourceType, string input, string firelyOutput)
        {
            string json = $$$"""{"resourceType":"{{{resourceType}}}","id":"p",{{{input}}}}""";
            Assert.Contains(firelyOutput.Trim(), ImportWrite(json, ImportMode.IncrementalLoad, _firely, true).Data);
            Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true));
        }

        [Fact]
        public void GivenGeneratedSchema_ThenFirelyMappingsCoverEveryCodeEnum()
        {
            IFhirSchemaProvider schema = _schemaContext.Schema;
            var types = (IReadOnlyDictionary<string, IType>)schema.GetType().GetField("_types", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).GetValue(schema);
            ModelInspector inspector = ModelInspector.ForAssembly(new VersionSpecificModelInfoProvider().GetTypeForFhirType("Patient").Assembly);
            Assert.Equal(
                schema.Version switch
                {
                    FhirVersion.Stu3 => 507,
                    FhirVersion.R4 => 682,
                    FhirVersion.R4B => 702,
                    FhirVersion.R5 => 859,
                    _ => throw new NotSupportedException(schema.Version.ToString()),
                },
                types.Count);

            var missing = new List<string>();
            int enumCount = 0;
            foreach (string name in types.Keys.Order(StringComparer.Ordinal))
            {
                string[] segments = name.Split('.');
                ClassMapping expected = ModelInfo.ModelInspector.FindClassMapping(segments[0]);
                foreach (string segment in segments.Skip(1))
                {
                    expected = expected?.FindMappedElementByName(segment)?.PropertyTypeMapping;
                }

                if (expected is null)
                {
                    missing.Add(name);
                    continue;
                }

                var enums = FirelyCompatibleResourceJsonWriter.FindCodeEnums(inspector, name);
                var enumProperties = expected.PropertyMappings
                    .Where(p => p.ImplementingType.IsConstructedGenericType && p.ImplementingType.GetGenericTypeDefinition() == typeof(Code<>))
                    .ToArray();
                Assert.Equal(enumProperties.Length, enums.Count);
                enumCount += enums.Count;
                foreach (PropertyMapping property in enumProperties)
                {
                    Assert.True(enums.TryGetValue(property.Name, out Type actual), $"{name}.{property.Name}");
                    Assert.Equal(property.ImplementingType.GenericTypeArguments[0], actual);
                }
            }

            Assert.True(missing.Count == 0, $"{schema.Version}: {types.Count} schema types; unmapped: {string.Join(", ", missing)}");
            Assert.True(enumCount > 0);
            Assert.Throws<InvalidOperationException>(() => FirelyCompatibleResourceJsonWriter.FindCodeEnums(inspector, "Patient.notARealBackbone"));
        }

        private static string[] FormatRejects() =>
        [
#if Stu3
            "InvalidObservation.json",
            "ObservationWithInvalidStatus.json",
#elif R4B
            "MedicinalProduct.json",
            "compartmentdefinition-device.json",
            "effectevidencesynthesis-example.json",
            "evidence-example.json",
            "medicinalproductauthorization-example.json",
            "medicinalproductindication-example.json",
            "medicinalproductingredient-example.json",
            "medicinalproductinteraction-example.json",
            "medicinalproductmanufactured-example.json",
            "medicinalproductpackaged-example.json",
            "medicinalproductpharmaceutical-example.json",
            "medicinalproductundesirableeffect-example.json",
            "riskevidencesynthesis-example.json",
            "substancenucleicacid-example.json",
            "substancepolymer-example.json",
            "substanceprotein-example.json",
            "substancereferenceinformation-example.json",
            "substancesourcematerial-example.json",
            "substancespecification-example.json",
#elif R5
            "SearchParameterInvalidBase.json",
            "SearchParameterInvalidType.json",
            "SearchParameterMissingType.json",
#endif
        ];

        private static string[] MissingIdRejects() =>
        [
#if Stu3
            "BloodGlucose.json",
            "BloodPressure.json",
            "Bundle-TransactionWithConditionalCreateAndReference.json",
            "Bundle-TransactionWithConditionalUpdateAndReference.json",
            "Expand-ValueSets.json",
            "Includes-TestResources.json",
            "PatientWithMinimalData.json",
            "SearchParameter-USCoreIG.json",
            "Weight.json",
            "WeightInGrams.json",
            "WeightInPounds.json",
#elif R5
            "Bundle-TransactionWithMetaHistory.json",
            "Includes-TestResources.json",
            "SearchDataBatch.json",
            "SearchParameter-USCoreIG.json",
#endif
        ];

        [Fact]
        public void GivenDifferentialCorpus_ThenEveryExpectedRejectionAndVersionFixtureIsSelected()
        {
            object[][] corpus = DifferentialCorpus().ToArray();
            var names = corpus.Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(corpus.Length, names.Count);
            Assert.All(FormatRejects().Concat(MissingIdRejects()), name => Assert.Contains(name, names));
#if Stu3
            Assert.Equal(83, names.Count);
#elif R4B
            Assert.Equal(168, names.Count);
            foreach (string fixture in new[] { "binary-example", "MedicinalProductDefinition", "examplescenario-example" })
            {
                string selected = (string)Assert.Single(corpus, row => (string)row[0] == fixture + ".json")[1];
                Assert.Equal(EmbeddedResourceManager.GetStringContent("TestFiles", fixture, "json"), selected);
            }
#elif R4
            Assert.Equal(167, names.Count);
#elif R5
            Assert.Equal(55, names.Count);
#endif
        }

        public static IEnumerable<object[]> DifferentialCorpus()
        {
            string version = new VersionSpecificModelInfoProvider().Version.ToString();
            var assembly = typeof(EmbeddedResourceManager).Assembly;
            string prefix = $"{typeof(EmbeddedResourceManager).Namespace}.TestFiles.{version}.";
            string fallback = $"{typeof(EmbeddedResourceManager).Namespace}.TestFiles.R4.";
            var names = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            foreach (string name in names.Order(StringComparer.Ordinal))
            {
                bool primary = name.StartsWith(prefix, StringComparison.Ordinal);
                if (!primary && (version != "R4B" || !name.StartsWith(fallback, StringComparison.Ordinal) || names.Contains(prefix + name[fallback.Length..])))
                {
                    continue;
                }

                using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
                string json = reader.ReadToEnd();
                if (IsSingleResource(json))
                {
                    yield return new object[] { name[(primary ? prefix.Length : fallback.Length)..], json, ImportMode.IncrementalLoad };
                }
            }

            int row = 0;
            foreach (var item in ImportResourceParserParityTests.AcceptedParityCorpus)
            {
                yield return new object[] { $"handwritten-{++row}", item.Data.Item1, item.Data.Item2 };
            }
        }

        [Theory]
        [MemberData(nameof(DifferentialCorpus), DisableDiscoveryEnumeration = true)]
        public void GivenDifferentialCorpus_ThenNativeMatchesIgnixaParserWithFirelyWriter(string name, string json, ImportMode mode)
            => AssertIgnixaWriteParity(name, json, mode);

        private static bool IsSingleResource(string json)
        {
            try
            {
                return JsonNode.Parse(json) is JsonObject node
                    && node["resourceType"] is JsonValue type
                    && type.TryGetValue<string>(out string name)
                    && name.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private void AssertIgnixaWriteParity(string name, string json, ImportMode mode)
        {
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                RawResource mixed = null;
                RawResource native = null;
                Exception mixedError = Record.Exception(() => mixed = ImportWrite(json, mode, _firely, useIgnixaParser: true));
                Exception nativeError = Record.Exception(() => native = ImportWrite(json, mode, native: true));
                if (FormatRejects().Contains(name))
                {
                    Assert.IsType<FormatException>(mixedError);
                    Assert.IsType<FormatException>(nativeError);
                    return;
                }

                if (MissingIdRejects().Contains(name))
                {
                    Assert.Contains("Invalid resource id", Assert.IsType<BadRequestException>(mixedError).Message);
                    Assert.Contains("Invalid resource id", Assert.IsType<BadRequestException>(nativeError).Message);
                    return;
                }

                Assert.True(mixedError is null && nativeError is null, $"{name}: IF={Describe(mixed, mixedError)}; II={Describe(native, nativeError)}");
                var expected = JsonNode.Parse(mixed.Data);
                var actual = JsonNode.Parse(native.Data);
                NormalizeDifferentialLiterals(expected, actual, mapping: null, root: true, path: name);
#if R4 || R4B
                if (name == "examplescenario-example.json")
                {
                    AssertExampleScenarioDiscriminatorDifference(JsonNode.Parse(json), expected, actual);
                }
#endif
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{name}: {FirstDifference(expected, actual, "$")}");
            }
        }

        private static string Describe(RawResource result, Exception error)
            => error is null ? result.Data : $"{error.GetType().Name}: {error.Message}";

#if R4 || R4B
        private static void AssertExampleScenarioDiscriminatorDifference(JsonNode input, JsonNode expected, JsonNode actual)
        {
            JsonArray instances = input["instance"].AsArray();
            Assert.Equal(instances.Count, expected["instance"].AsArray().Count);
            Assert.Equal(instances.Count, actual["instance"].AsArray().Count);
            for (int i = 0; i < instances.Count; i++)
            {
                var ifInstance = expected["instance"][i].AsObject();
                var iiInstance = actual["instance"][i].AsObject();
                Assert.False(ifInstance.ContainsKey("resourceType"), $"IF unexpectedly kept instance[{i}].resourceType");
                Assert.Equal(instances[i]["resourceType"].GetValue<string>(), iiInstance["resourceType"].GetValue<string>());
                iiInstance.Remove("resourceType");
            }
        }
#endif

        private static void NormalizeDifferentialLiterals(JsonNode expected, JsonNode actual, ClassMapping mapping, string path, bool root = false, bool rootMeta = false)
        {
            if (expected is JsonObject left && actual is JsonObject right)
            {
                if ((root || mapping?.NativeType == typeof(Resource) || mapping?.NativeType == typeof(DomainResource))
                    && TryGetText(left["resourceType"], out string resourceType))
                {
                    mapping = ModelInfo.ModelInspector.FindClassMapping(resourceType);
                }

                foreach ((string key, JsonNode value) in left)
                {
                    ClassMapping child = FindCorpusChild(mapping, key);
                    if (TryGetText(value, out string leftText) && TryGetText(right[key], out string rightText)
                        && EquivalentLiteral(leftText, rightText, child?.NativeType, rootMeta && key == "lastUpdated"))
                    {
                        right[key] = leftText;
                    }
                    else
                    {
                        NormalizeDifferentialLiterals(value, right[key], child, $"{path}.{key}", rootMeta: root && key == "meta");
                    }
                }
            }
            else if (expected is JsonArray leftItems && actual is JsonArray rightItems && leftItems.Count == rightItems.Count)
            {
                for (int i = 0; i < leftItems.Count; i++)
                {
                    if (TryGetText(leftItems[i], out string leftText) && TryGetText(rightItems[i], out string rightText)
                        && EquivalentLiteral(leftText, rightText, mapping?.NativeType, rootLastUpdated: false))
                    {
                        rightItems[i] = leftText;
                    }
                    else
                    {
                        NormalizeDifferentialLiterals(leftItems[i], rightItems[i], mapping, $"{path}[{i}]");
                    }
                }
            }
            else if (expected is JsonValue leftValue && actual is JsonValue rightValue
                && leftValue.TryGetValue(out JsonElement leftNumber) && leftNumber.ValueKind == JsonValueKind.Number
                && rightValue.TryGetValue(out JsonElement rightNumber) && rightNumber.ValueKind == JsonValueKind.Number
                && leftNumber.GetRawText() != rightNumber.GetRawText())
            {
                Assert.True(
                    mapping?.NativeType == typeof(FhirDecimal)
                        && leftNumber.TryGetDecimal(out decimal leftDecimal)
                        && rightNumber.TryGetDecimal(out decimal rightDecimal)
                        && leftDecimal == rightDecimal,
                    $"{path}: IF={leftNumber.GetRawText()}; II={rightNumber.GetRawText()}");
                actual.ReplaceWith(expected.DeepClone());
            }
        }

        private static ClassMapping FindCorpusChild(ClassMapping parent, string key)
        {
            if (parent is null)
            {
                return null;
            }

            var property = parent.FindMappedElementByName(key.TrimStart('_'));
            if (property is not null)
            {
                return property.PropertyTypeMapping;
            }

            if (!key.StartsWith('_') && parent.PropertyMappings.FirstOrDefault(p => p.Choice == ChoiceType.DatatypeChoice
                && key.Length > p.Name.Length && key.StartsWith(p.Name, StringComparison.Ordinal)) is { } choice)
            {
                string suffix = key[choice.Name.Length..];
                return ModelInfo.ModelInspector.FindClassMapping(suffix)
                    ?? ModelInfo.ModelInspector.FindClassMapping(char.ToLowerInvariant(suffix[0]) + suffix[1..]);
            }

            return null;
        }

        private static bool EquivalentLiteral(string expected, string actual, Type kind, bool rootLastUpdated)
        {
            if (expected == actual)
            {
                return false;
            }

            if (kind == typeof(Instant))
            {
                return !rootLastUpdated
                    && DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.None, out var left)
                    && DateTimeOffset.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.None, out var right)
                    && left.EqualsExact(right);
            }

            return (kind == typeof(FhirString) || kind == typeof(Markdown) || kind == typeof(XHtml)) && expected == actual.Trim();
        }

        private static bool TryGetText(JsonNode node, out string text)
        {
            text = null;
            return node is JsonValue value && value.TryGetValue(out text);
        }

        private static string FirstDifference(JsonNode expected, JsonNode actual, string path)
        {
            if (expected is JsonObject left && actual is JsonObject right)
            {
                foreach (string key in left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key)))
                {
                    if (left.ContainsKey(key) != right.ContainsKey(key) || !JsonNode.DeepEquals(left[key], right[key]))
                    {
                        return FirstDifference(left[key], right[key], $"{path}.{key}");
                    }
                }
            }
            else if (expected is JsonArray leftItems && actual is JsonArray rightItems && leftItems.Count == rightItems.Count)
            {
                for (int i = 0; i < leftItems.Count; i++)
                {
                    if (!JsonNode.DeepEquals(leftItems[i], rightItems[i]))
                    {
                        return FirstDifference(leftItems[i], rightItems[i], $"{path}[{i}]");
                    }
                }
            }

            return $"{path}: IF={expected?.ToJsonString() ?? "(absent or null)"}; II={actual?.ToJsonString() ?? "(absent or null)"}";
        }

        [Fact]
        public void GivenEmptyGender_ThenBothIgnixaImportPathsReject()
        {
            const string json = """{"resourceType":"Patient","id":"p","gender":""}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                Assert.Contains("AdministrativeGender", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true)).Message);
                Assert.DoesNotContain("\"gender\"", ImportWrite(json, ImportMode.IncrementalLoad, native: false).Data);

                Assert.Contains("Patient.gender", Assert.Throws<FormatException>(() => ImportWrite(json, ImportMode.IncrementalLoad, native: true)).Message);
            }
        }

        [Theory]
        [InlineData(""" "birthDate":"" """)]
        [InlineData(""" "text":{"status":"generated","div":""} """)]
        [InlineData(""" "maritalStatus":{"coding":[{"code":""}]} """)]
        public void GivenEmptyNonEnumPrimitiveAcceptedByIgnixaToFirely_ThenNativeWritesIt(string property)
        {
            string json = $$$"""{"resourceType":"Patient","id":"p",{{{property}}}}""";
            using (Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(PinnedNow)))
            {
                string mixed = ImportWrite(json, ImportMode.IncrementalLoad, _firely, useIgnixaParser: true).Data;
                Assert.Contains(property.Trim(), mixed);
                AssertExactly(mixed, ImportWrite(json, ImportMode.IncrementalLoad, native: true).Data);
            }
        }

        private static string Version(string data)
        {
            using var json = JsonDocument.Parse(data);
            return json.RootElement.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("versionId", out JsonElement version)
                ? version.GetString()
                : null;
        }

        private static void AssertExactly(string expected, string actual)
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Firely: {expected}\nIgnixa: {actual}");
            Assert.True(FirelyParser.Parse<Resource>(expected).IsExactly(FirelyParser.Parse<Resource>(actual)), $"Firely: {expected}\nIgnixa: {actual}");
        }

        private RawResource FirelyWrite(string json, bool keepMeta = true, bool keepVersion = true)
            => _firely.Create(FirelyParser.Parse<Resource>(json).ToResourceElement(), keepMeta, keepVersion);

        private ResourceElement Native(string json)
        {
            var node = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
            return new ResourceElement(node.ToElement(_schemaContext.Schema).ToTypedElement(), node);
        }

        private RawResource ImportWrite(string json, ImportMode mode, bool native)
            => ImportWrite(json, mode, native ? Factory : _firely, useIgnixaParser: native);

        private RawResource ImportWrite(string json, ImportMode mode, IRawResourceFactory factory, bool useIgnixaParser)
        {
            ResourceElement element = null;
            var wrapper = Substitute.For<IResourceWrapperFactory>();
            wrapper.Create(Arg.Do<ResourceElement>(value => element = value), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>());
            IImportResourceParser parser = useIgnixaParser
                ? new IgnixaImportResourceParser(wrapper, _schemaContext, ResourceIdPolicy.Standard)
                : new FirelyImportResourceParser(FirelyParser, wrapper, ResourceIdPolicy.Standard);
            ImportResource result = parser.Parse(0, 0, json.Length, json, mode);
            return factory.Create(element, true, result.KeepVersion);
        }
    }
}
