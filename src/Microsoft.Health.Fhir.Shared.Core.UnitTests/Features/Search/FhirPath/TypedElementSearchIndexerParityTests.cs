// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search.FhirPath
{
    /// <summary>
    /// Runs the real <see cref="TypedElementSearchIndexer"/> under both FHIR SDK providers and compares the
    /// <see cref="SearchIndexEntry"/> output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test in this area is a proxy for this one. Comparing evaluator results in isolation cannot see
    /// a difference in converter selection, in composite component pairing, in the <c>Distinct()</c> collapse, or
    /// in the min/max sort flags <c>ExtractMinAndMaxValues</c> sets - all of which are what actually reaches the
    /// database. This is the assertion that protects the search index.
    /// </para>
    /// <para>
    /// The Ignixa arm is driven from <see cref="IgnixaImportResourceParser"/> rather than from a Firely
    /// deserializer, so the element under test is the native, Ignixa-backed one that $import really produces.
    /// Both arms share one converter manager, reference resolver and definition manager, leaving the FHIRPath
    /// engine as the only variable.
    /// </para>
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [Trait(Traits.Category, Categories.Search)]
    public class TypedElementSearchIndexerParityTests
    {
        static TypedElementSearchIndexerParityTests()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        public static TheoryData<string, string> Corpus() => new()
        {
            {
                "patient",
                """
                {"resourceType":"Patient","id":"p1","meta":{"versionId":"3","lastUpdated":"1990-01-02T03:04:05.123+00:00"},
                 "identifier":[{"system":"http://example.org/id","value":"abc"},{"system":"http://example.org/id2","value":"def"}],
                 "active":true,
                 "name":[{"use":"official","family":"Chalmers","given":["Peter","James"]},{"use":"usual","given":["Jim"]}],
                 "telecom":[{"system":"phone","value":"555-1234","use":"home"},{"system":"email","value":"a@b.example"}],
                 "gender":"male","birthDate":"1974-12-25",
                 "address":[{"use":"home","line":["534 Erewhon St"],"city":"PleasantVille","state":"Vic","postalCode":"3999"}],
                 "managingOrganization":{"reference":"Organization/1"}}
                """
            },
            {
                "observation with quantity and reference",
                """
                {"resourceType":"Observation","id":"o1","meta":{"versionId":"1","lastUpdated":"1990-01-02T03:04:05.123+00:00"},
                 "status":"final",
                 "category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"vital-signs"}]}],
                 "code":{"coding":[{"system":"http://loinc.org","code":"29463-7","display":"Body Weight"}],"text":"Weight"},
                 "subject":{"reference":"Patient/p1"},
                 "effectiveDateTime":"1990-01-02",
                 "valueQuantity":{"value":72.5,"unit":"kg","system":"http://unitsofmeasure.org","code":"kg"}}
                """
            },
            {
                "observation with period and multiple codings",
                """
                {"resourceType":"Observation","id":"o2","meta":{"versionId":"1","lastUpdated":"1990-01-02T03:04:05.123+00:00"},
                 "status":"amended",
                 "code":{"coding":[{"system":"http://loinc.org","code":"1"},{"system":"http://snomed.info/sct","code":"2"}]},
                 "subject":{"reference":"Patient/p1"},
                 "effectivePeriod":{"start":"1990-01-01","end":"1990-01-31"}}
                """
            },
            {
                "patient minimal",
                """
                {"resourceType":"Patient","id":"p2","meta":{"versionId":"1","lastUpdated":"1990-01-02T03:04:05.123+00:00"}}
                """
            },
        };

        [Theory]
        [MemberData(nameof(Corpus))]
        public async Task GivenAResource_WhenIndexedUnderBothProviders_ThenTheSearchIndexEntriesMatch(string name, string json)
        {
            SearchParameterDefinitionManager definitionManager =
                await SearchParameterFixtureData.CreateSearchParameterDefinitionManagerAsync(
                    new VersionSpecificModelInfoProvider(),
                    Substitute.For<Medino.IMediator>());

            var supported = new SupportedSearchParameterDefinitionManager(definitionManager);
            var converterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();

            // The real resolver, not a substitute: several stock parameters use resolve(), and a resolver that
            // returns null makes Firely's resolve() throw. Production wires this same type, and both arms must
            // share one instance so the engine stays the only variable.
            var instanceConfiguration = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfiguration.BaseUri.Returns(new Uri("https://localhost/"));

            IReferenceToElementResolver referenceResolver = new LightweightReferenceToElementResolver(
                new ReferenceSearchValueParser(new FhirRequestContextAccessor(), instanceConfiguration),
                ModelInfoProvider.Instance);

            ISearchIndexer firelyIndexer = CreateIndexer(supported, converterManager, referenceResolver, new FirelyFhirPathEvaluator());
            ISearchIndexer ignixaIndexer = CreateIndexer(supported, converterManager, referenceResolver, new IgnixaFhirPathEvaluator());

            ResourceElement firelyResource = ParseWithFirely(json);
            ResourceElement ignixaResource = ParseWithIgnixa(json);

            IReadOnlyCollection<SearchIndexEntry> firely = firelyIndexer.Extract(firelyResource);
            IReadOnlyCollection<SearchIndexEntry> ignixa = ignixaIndexer.Extract(ignixaResource);

            Assert.NotEmpty(firely);

            HashSet<string> firelySet = firely.Select(Describe).ToHashSet(StringComparer.Ordinal);
            HashSet<string> ignixaSet = ignixa.Select(Describe).ToHashSet(StringComparer.Ordinal);

            string[] onlyFirely = firelySet.Except(ignixaSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] onlyIgnixa = ignixaSet.Except(firelySet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            string message =
                $"{name}: search index entries differ." + Environment.NewLine +
                "only Firely:" + Environment.NewLine + string.Join(Environment.NewLine, onlyFirely) + Environment.NewLine +
                "only Ignixa:" + Environment.NewLine + string.Join(Environment.NewLine, onlyIgnixa);

            Assert.True(onlyFirely.Length == 0 && onlyIgnixa.Length == 0, message);
        }

        private static ISearchIndexer CreateIndexer(
            ISupportedSearchParameterDefinitionManager definitionManager,
            ITypedElementToSearchValueConverterManager converterManager,
            IReferenceToElementResolver referenceResolver,
            IFhirPathEvaluator evaluator)
        {
            return new TypedElementSearchIndexer(
                definitionManager,
                converterManager,
                referenceResolver,
                ModelInfoProvider.Instance,
                evaluator,
                NullLogger<TypedElementSearchIndexer>.Instance);
        }

        /// <summary>
        /// Renders an entry so two collections can be compared as sets, including the sort flags
        /// <c>ExtractMinAndMaxValues</c> sets - a difference in those changes sorting behaviour without changing
        /// any value.
        /// </summary>
        private static string Describe(SearchIndexEntry entry)
        {
            string min = entry.Value is ISupportSortSearchValue sortable && sortable.IsMin ? "|min" : string.Empty;
            string max = entry.Value is ISupportSortSearchValue sortableMax && sortableMax.IsMax ? "|max" : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.SearchParameter.Url}|{entry.Value.GetType().Name}|{entry.Value}{min}{max}");
        }

        private static ResourceElement ParseWithFirely(string json)
        {
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);

            return Deserializers.ResourceDeserializer.DeserializeRaw(
                rawResource,
                "1",
                new DateTimeOffset(1990, 1, 2, 3, 4, 5, 123, TimeSpan.Zero));
        }

        /// <summary>
        /// Produces the element $import really indexes: parsed by Ignixa and still backed by its native node.
        /// </summary>
        private static ResourceElement ParseWithIgnixa(string json)
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            var capturing = new CapturingRawResourceFactory();

            var wrapperFactory = new ResourceWrapperFactory(
                capturing,
                requestContextAccessor,
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                Substitute.For<ISearchParameterDefinitionManager>(),
                Deserializers.ResourceDeserializer);

            var parser = new IgnixaImportResourceParser(
                wrapperFactory,
                new IgnixaSchemaContext(new VersionSpecificModelInfoProvider()));

            parser.Parse(0, 0, json.Length, json, ImportMode.IncrementalLoad);

            return capturing.LastResource;
        }

        private sealed class CapturingRawResourceFactory : IRawResourceFactory
        {
            public ResourceElement LastResource { get; private set; }

            public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
            {
                LastResource = resource;
                return new RawResource("{}", FhirResourceFormat.Json, keepMeta);
            }
        }
    }
}
