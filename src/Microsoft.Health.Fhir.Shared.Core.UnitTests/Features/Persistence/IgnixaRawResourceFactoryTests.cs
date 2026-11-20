// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
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

        private IgnixaRawResourceFactory Factory => new(_inner, _schemaContext);

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
            AssertExactly(FirelyWrite(json, false).Data, data);
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
        public void GivenRejectedCorpus_ThenBothProvidersThrowExpectedType(string json, ImportMode mode, Type exceptionType)
        {
            Assert.Throws(exceptionType, () => ImportWrite(json, mode, native: false));
            Assert.Throws(exceptionType, () => ImportWrite(json, mode, native: true));
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
        {
            ResourceElement element = null;
            var wrapper = Substitute.For<IResourceWrapperFactory>();
            wrapper.Create(Arg.Do<ResourceElement>(value => element = value), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>());
            IImportResourceParser parser = native
                ? new IgnixaImportResourceParser(wrapper, _schemaContext, ResourceIdPolicy.Standard)
                : new FirelyImportResourceParser(FirelyParser, wrapper, ResourceIdPolicy.Standard);
            ImportResource result = parser.Parse(0, 0, json.Length, json, mode);
            IRawResourceFactory factory = native ? Factory : _firely;
            return factory.Create(element, true, result.KeepVersion);
        }
    }
}
