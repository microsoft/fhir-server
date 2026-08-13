// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json.Nodes;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Persistence
{
    /// <summary>
    /// Byte-parity tests for the raw resource produced by the Firely and Ignixa
    /// <see cref="IRawResourceFactory"/> implementations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw resource is persisted and handed back to clients verbatim, so a serializer difference is
    /// directly observable to callers rather than being an internal detail. These tests drive the Ignixa
    /// factory the way $import does - from a resource parsed by <see cref="IgnixaImportResourceParser"/> -
    /// because that is the only path on which the native JSON document is available.
    /// </para>
    /// <para>
    /// The cases deliberately cover the character classes where <c>System.Text.Json</c>'s default escaper
    /// diverges from Firely (<c>+</c> in a timezone offset, <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, apostrophes,
    /// non-ASCII text and embedded XHTML narrative) and the decimal precision FHIR requires be preserved.
    /// </para>
    /// <para>
    /// The <c>lastUpdated</c> values are deliberately far in the past: the parser rejects a <c>lastUpdated</c>
    /// more than ten seconds ahead of <c>Clock.UtcNow</c>, and other tests in this assembly temporarily install
    /// a fake time provider through a static property that is visible to tests running in parallel.
    /// </para>
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class RawResourceFactoryParityTests
    {
        private readonly IRawResourceFactory _firelyRawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
        private readonly IRawResourceFactory _ignixaRawResourceFactory;
        private readonly CapturingRawResourceFactory _capturingRawResourceFactory;
        private readonly IImportResourceParser _ignixaParser;

        public RawResourceFactoryParityTests()
        {
            _ignixaRawResourceFactory = new IgnixaRawResourceFactory(_firelyRawResourceFactory);
            _capturingRawResourceFactory = new CapturingRawResourceFactory(_firelyRawResourceFactory);

            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            var wrapperFactory = new ResourceWrapperFactory(
                _capturingRawResourceFactory,
                requestContextAccessor,
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                Substitute.For<ISearchParameterDefinitionManager>(),
                Deserializers.ResourceDeserializer);

            _ignixaParser = new IgnixaImportResourceParser(
                wrapperFactory,
                new IgnixaSchemaContext(new VersionSpecificModelInfoProvider()));
        }

        public static TheoryData<string, string> Corpus() => new()
        {
            {
                "timezone offset",
                """{"resourceType":"Patient","id":"a","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00"},"active":true}"""
            },
            {
                "negative offset",
                """{"resourceType":"Patient","id":"b","meta":{"versionId":"7","lastUpdated":"1990-06-01T10:00:00.250-07:00"},"active":true}"""
            },
            {
                "punctuation and non-ascii",
                """{"resourceType":"Patient","id":"c","meta":{"versionId":"2","lastUpdated":"1990-01-02T03:04:05.001+00:00"},"name":[{"family":"O'Brien & Sons <Ltd>","given":["Zo\u00eb","\u4e2d\u6587"]}]}"""
            },
            {
                "xhtml narrative",
                """{"resourceType":"Patient","id":"d","meta":{"versionId":"2","lastUpdated":"1990-01-02T03:04:05.001+00:00"},"text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">a &amp; b &lt;c&gt;</div>"}}"""
            },
            {
                "decimal precision",
                """{"resourceType":"Observation","id":"e","meta":{"versionId":"2","lastUpdated":"1990-01-02T03:04:05.001+00:00"},"status":"final","code":{"text":"x"},"valueQuantity":{"value":1.10,"unit":"mg"}}"""
            },
            {
                "tiny decimal",
                """{"resourceType":"Observation","id":"f","meta":{"versionId":"2","lastUpdated":"1990-01-02T03:04:05.001+00:00"},"status":"final","code":{"text":"x"},"valueQuantity":{"value":0.000000000000000000001,"unit":"mg"}}"""
            },
        };

        [Fact]
        public void GivenAnIgnixaParsedResource_WhenSerialized_ThenTheFirelyFactoryIsNotUsed()
        {
            // The native path is the entire point of this factory, and nothing else in the suite proves it is
            // taken: every other assertion compares output, and the fallback produces identical output by
            // construction. If the ToTypedElement()/ToIgnixaElement() round trip were broken, the factory would
            // silently degrade to rebuilding a Firely POCO per resource - correct, but with the performance win
            // gone and no test failing. Counting inner calls is what makes that regression visible.
            var counting = new CountingRawResourceFactory(_firelyRawResourceFactory);
            var factory = new IgnixaRawResourceFactory(counting);

            const string Json = """{"resourceType":"Patient","id":"a","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00"},"active":true}""";

            RawResource result = factory.Create(ParseWithIgnixa(Json), keepMeta: true, keepVersion: true);

            Assert.Equal(0, counting.CallCount);
            Assert.Contains("\"resourceType\":\"Patient\"", result.Data, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenAFirelyBackedResource_WhenSerialized_ThenTheFirelyFactoryIsUsed(bool keepMeta, bool keepVersion)
        {
            // The mirror of the above: a resource that never went through Ignixa's parser has no JSON document
            // to reuse, so the inner factory must actually be invoked rather than the factory inventing output.
            var counting = new CountingRawResourceFactory(_firelyRawResourceFactory);
            var factory = new IgnixaRawResourceFactory(counting);

            RawResource ignixa = factory.Create(Samples.GetDefaultPatient(), keepMeta, keepVersion);
            RawResource firely = _firelyRawResourceFactory.Create(Samples.GetDefaultPatient(), keepMeta, keepVersion);

            Assert.Equal(1, counting.CallCount);
            Assert.Equal(firely.Data, ignixa.Data);
        }

        [Fact]
        public void GivenASoftDeletedResource_WhenSerializedNatively_ThenTheSoftDeleteExtensionIsNotPersisted()
        {
            // The parser strips the soft-delete marker by mutating the JSON and calling InvalidateCaches(). That
            // this factory then sees the POST-mutation document is an assumption, not something the import parity
            // tests can check - they wire the Firely serializer for both arms. If it saw a stale node the resource
            // would be stored as deleted AND still carrying the marker.
            const string Json = """
                {"resourceType":"Patient","id":"a","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00","extension":[{"url":"http://azurehealthcareapis.com/data-extensions/deleted-state","valueString":"soft-deleted"}]},"active":true}
                """;

            ResourceElement resource = ParseWithIgnixa(Json);

            RawResource firely = _firelyRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);
            RawResource ignixa = _ignixaRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);

            Assert.DoesNotContain("deleted-state", ignixa.Data, StringComparison.Ordinal);
            Assert.True(
                string.Equals(firely.Data, ignixa.Data, StringComparison.Ordinal),
                FormatMismatch("soft deleted", firely.Data, ignixa.Data));
        }

        [Fact]
        public void GivenContainedBeforeMeta_WhenSerialized_ThenRootMetadataStillLeadsTheDocument()
        {
            // SqlServerFhirDataStore manipulates the stored JSON as a string on the $import write path:
            // GetJsonValue takes the FIRST "versionId":" in the payload and SyncVersionIdInMeta then runs a
            // global string.Replace on it. A contained resource carrying its own meta.versionId ahead of the
            // root meta would therefore have the CONTAINED version rewritten - silent corruption of stored data.
            // The Firely factory cannot hit this because re-serializing a POCO always emits root meta before
            // contained; this factory echoes the source document, so it hoists the root metadata explicitly.
            const string Json = """
                {"resourceType":"Patient","contained":[{"resourceType":"Organization","id":"org","meta":{"versionId":"99"}}],"id":"a","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00"},"active":true}
                """;

            RawResource ignixa = _ignixaRawResourceFactory.Create(ParseWithIgnixa(Json), keepMeta: true, keepVersion: true);

            int rootVersion = ignixa.Data.IndexOf("\"versionId\":\"7\"", StringComparison.Ordinal);
            int containedVersion = ignixa.Data.IndexOf("\"versionId\":\"99\"", StringComparison.Ordinal);

            Assert.True(rootVersion >= 0, $"root versionId missing: {ignixa.Data}");
            Assert.True(containedVersion >= 0, $"contained versionId missing: {ignixa.Data}");
            Assert.True(
                rootVersion < containedVersion,
                $"root versionId must precede the contained one or SQL rewrites the wrong node: {ignixa.Data}");

            // The same invariant ChangesAreOnlyInMetadata depends on.
            Assert.True(
                ignixa.Data.IndexOf("\"meta\":", StringComparison.Ordinal) <
                ignixa.Data.IndexOf("\"contained\":", StringComparison.Ordinal),
                $"root meta must precede contained: {ignixa.Data}");
        }

        [Fact]
        public void GivenShuffledRootProperties_WhenSerializedByBothFactories_ThenTheDocumentsAreEquivalentAndMetadataLeads()
        {
            // Real NDJSON does not arrive in canonical order. Firely re-serializes a POCO and so normalises the
            // order; this factory preserves the source order apart from the hoisted metadata. The documents are
            // therefore equivalent but not byte-identical, which this pins deliberately rather than avoiding.
            const string Json = """
                {"active":true,"birthDate":"1974-12-25","resourceType":"Patient","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00"},"id":"a"}
                """;

            RawResource firely = _firelyRawResourceFactory.Create(ParseWithIgnixa(Json), keepMeta: true, keepVersion: true);
            RawResource ignixa = _ignixaRawResourceFactory.Create(ParseWithIgnixa(Json), keepMeta: true, keepVersion: true);

            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(firely.Data), JsonNode.Parse(ignixa.Data)),
                FormatMismatch("shuffled", firely.Data, ignixa.Data));

            Assert.StartsWith("{\"resourceType\":\"Patient\",\"id\":\"a\",\"meta\":", ignixa.Data, StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void GivenAnImportedResource_WhenSerializedByBothFactories_ThenTheRawResourceBytesMatch(string name, string json)
        {
            ResourceElement resource = ParseWithIgnixa(json);

            RawResource firely = _firelyRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);
            RawResource ignixa = _ignixaRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);

            string message = FormatMismatch(name, firely.Data, ignixa.Data);

            Assert.True(string.Equals(firely.Data, ignixa.Data, StringComparison.Ordinal), message);
            Assert.Equal(firely.Format, ignixa.Format);
            Assert.Equal(firely.IsMetaSet, ignixa.IsMetaSet);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenTheMetaAndVersionFlags_WhenSerializedByBothFactories_ThenVersionIdIsHandledIdentically(bool keepMeta, bool keepVersion)
        {
            const string Json = """{"resourceType":"Patient","id":"a","meta":{"versionId":"7","lastUpdated":"1990-01-02T03:04:05.678+00:00"},"active":true}""";

            // Each factory gets its own parse, because Create deliberately mutates meta.versionId in place and
            // only restores it when keepMeta is false - behaviour the Ignixa factory mirrors exactly.
            RawResource firely = _firelyRawResourceFactory.Create(ParseWithIgnixa(Json), keepMeta, keepVersion);
            RawResource ignixa = _ignixaRawResourceFactory.Create(ParseWithIgnixa(Json), keepMeta, keepVersion);

            string message = FormatMismatch($"keepMeta={keepMeta}, keepVersion={keepVersion}", firely.Data, ignixa.Data);

            Assert.True(string.Equals(firely.Data, ignixa.Data, StringComparison.Ordinal), message);
            Assert.Equal(firely.IsMetaSet, ignixa.IsMetaSet);
        }

        [Fact]
        public void GivenAResourceWithoutMeta_WhenSerializedByBothFactories_ThenTheDocumentsAreEquivalent()
        {
            const string Json = """{"resourceType":"Patient","id":"no-meta","active":true}""";

            // Parsed once and shared: the parser stamps meta.lastUpdated from the clock when the resource has
            // none, so two separate parses would differ by however long they took.
            ResourceElement resource = ParseWithIgnixa(Json);

            RawResource firely = _firelyRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);
            RawResource ignixa = _ignixaRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);

            // A meta object created for a resource that had none is appended to the end of the document by
            // System.Text.Json, where Firely emits it immediately after id. The documents are equivalent but the
            // property order differs, so this case is asserted semantically rather than byte for byte.
            string message = FormatMismatch("no meta", firely.Data, ignixa.Data);
            bool equivalent = JsonNode.DeepEquals(JsonNode.Parse(firely.Data), JsonNode.Parse(ignixa.Data));

            Assert.True(equivalent, message);
        }

        [Fact]
        public void GivenAFirelyBackedResource_WhenSerializedByTheIgnixaFactory_ThenItFallsBackToFirely()
        {
            // A resource that never went through Ignixa's parser has no JSON document to reuse, so the Ignixa
            // factory must defer to Firely rather than fail.
            ResourceElement resource = Samples.GetDefaultPatient();

            RawResource firely = _firelyRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);
            RawResource ignixa = _ignixaRawResourceFactory.Create(resource, keepMeta: true, keepVersion: true);

            Assert.Equal(firely.Data, ignixa.Data);
        }

        private static string FormatMismatch(string name, string firely, string ignixa)
        {
            return $"{name}{Environment.NewLine}firely: {firely}{Environment.NewLine}ignixa: {ignixa}";
        }

        /// <summary>
        /// Parses through Ignixa and returns the <see cref="ResourceElement"/> the wrapper factory handed to the
        /// raw resource factory, which is exactly the element $import produces for a parsed line.
        /// </summary>
        private ResourceElement ParseWithIgnixa(string json)
        {
            _ignixaParser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);
            return _capturingRawResourceFactory.LastResource;
        }

        private sealed class CountingRawResourceFactory : IRawResourceFactory
        {
            private readonly IRawResourceFactory _inner;

            public CountingRawResourceFactory(IRawResourceFactory inner)
            {
                _inner = inner;
            }

            public int CallCount { get; private set; }

            public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
            {
                CallCount++;
                return _inner.Create(resource, keepMeta, keepVersion);
            }
        }

        private sealed class CapturingRawResourceFactory : IRawResourceFactory
        {
            private readonly IRawResourceFactory _inner;

            public CapturingRawResourceFactory(IRawResourceFactory inner)
            {
                _inner = inner;
            }

            public ResourceElement LastResource { get; private set; }

            public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
            {
                LastResource = resource;
                return _inner.Create(resource, keepMeta, keepVersion);
            }
        }
    }
}
