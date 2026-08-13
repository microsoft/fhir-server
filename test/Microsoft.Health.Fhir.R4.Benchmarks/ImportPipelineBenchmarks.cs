// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;

namespace Microsoft.Health.Fhir.Benchmarks
{
    /// <summary>
    /// Measures the two per-resource costs of <c>$import</c> under each FHIR SDK provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ResourceWrapperFactory.Create</c> does exactly two expensive things for every imported resource: it
    /// calls <see cref="IRawResourceFactory"/> to produce the stored JSON, and <c>ISearchIndexer.Extract</c>,
    /// which evaluates every supported search parameter expression through the FHIRPath engine. Those two are
    /// benchmarked separately here so a change in one is not hidden by the other, alongside the parse that
    /// precedes them.
    /// </para>
    /// <para>
    /// Expression evaluation stands in for the indexer itself: building a real
    /// <c>SearchParameterDefinitionManager</c> requires most of the server's DI graph, and the indexer's own
    /// work outside FHIRPath is provider-independent. The expression set below is representative of what the
    /// indexer evaluates per resource.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    [Config(typeof(Config))]
    public class ImportPipelineBenchmarks
    {
        /// <summary>
        /// Runs in process rather than letting BenchmarkDotNet generate and build a standalone project. The
        /// generated project inherits this repository's <c>Directory.Build.props</c> and central package
        /// management, which it cannot restore on its own.
        /// </summary>
        private sealed class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
            }
        }
        private static readonly string[] Expressions =
        {
            "Patient.id",
            "Patient.active",
            "Patient.name",
            "Patient.name.family",
            "Patient.name.given",
            "Patient.birthDate",
            "Patient.gender",
            "Patient.telecom",
            "Patient.telecom.where(system='phone')",
            "Patient.address",
            "Patient.address.city",
            "Patient.address.postalCode",
            "Patient.identifier",
            "Patient.managingOrganization",
            "Patient.generalPractitioner",
            "Patient.deceased.exists()",
            "Patient.meta.lastUpdated",
            "Patient.name.where(use='official').family",
        };

        private string _json;
        private IgnixaSchemaContext _schemaContext;

        private IRawResourceFactory _firelyRawResourceFactory;
        private IRawResourceFactory _ignixaRawResourceFactory;

        private IFhirPathEvaluator _firelyEvaluator;
        private IFhirPathEvaluator _ignixaEvaluator;

        private ResourceElement _ignixaParsedResource;
        private ResourceElement _firelyParsedResource;

        [GlobalSetup]
        public void Setup()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();

            _json = BuildPatientJson();
            _schemaContext = new IgnixaSchemaContext(new Core.VersionSpecificModelInfoProvider());

            _firelyRawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            _ignixaRawResourceFactory = new IgnixaRawResourceFactory(_firelyRawResourceFactory);

            _firelyEvaluator = new FirelyFhirPathEvaluator();
            _ignixaEvaluator = new IgnixaFhirPathEvaluator();

            _ignixaParsedResource = ParseWithIgnixa(_json);
            _firelyParsedResource = ParseWithFirely(_json);

            // Warm both expression caches so the measurements are steady-state, matching a running import job.
            foreach (string expression in Expressions)
            {
                _firelyEvaluator.Compile(expression);
                _ignixaEvaluator.Compile(expression);
            }
        }

        [Benchmark(Baseline = true, Description = "Firely: parse")]
        public ResourceElement FirelyParse() => ParseWithFirely(_json);

        [Benchmark(Description = "Ignixa: parse")]
        public ResourceElement IgnixaParse() => ParseWithIgnixa(_json);

        [Benchmark(Description = "Firely: serialize raw resource")]
        public RawResource FirelySerialize() =>
            _firelyRawResourceFactory.Create(_firelyParsedResource, keepMeta: true, keepVersion: true);

        [Benchmark(Description = "Ignixa: serialize raw resource")]
        public RawResource IgnixaSerialize() =>
            _ignixaRawResourceFactory.Create(_ignixaParsedResource, keepMeta: true, keepVersion: true);

        [Benchmark(Description = "Firely: evaluate search expressions")]
        public int FirelyEvaluate() => Evaluate(_firelyEvaluator, _firelyParsedResource);

        [Benchmark(Description = "Ignixa: evaluate search expressions")]
        public int IgnixaEvaluate() => Evaluate(_ignixaEvaluator, _ignixaParsedResource);

        private static int Evaluate(IFhirPathEvaluator evaluator, ResourceElement resource)
        {
            ITypedElement instance = resource.Instance;
            var context = new EvaluationContext { Resource = instance, RootResource = instance };

            int count = 0;

            foreach (string expression in Expressions)
            {
                foreach (ITypedElement result in evaluator.Compile(expression).Evaluate(instance, context))
                {
                    // Touch the value so lazily evaluated pipelines are actually driven to completion.
                    count += result.Value == null ? 1 : 2;
                }
            }

            return count;
        }

        private ResourceElement ParseWithIgnixa(string json)
        {
            ResourceJsonNode node = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
            IElement element = node.ToElement(_schemaContext.Schema);
            return new ResourceElement(element.ToTypedElement());
        }

        private static ResourceElement ParseWithFirely(string json)
        {
            return new FhirJsonParser().Parse<Hl7.Fhir.Model.Resource>(json).ToResourceElement();
        }

        private static string BuildPatientJson()
        {
            var identifiers = string.Join(
                ",",
                Enumerable.Range(0, 4).Select(i =>
                    $$"""{"system":"http://example.org/id/{{i}}","value":"id-{{i}}"}"""));

            return $$"""
                {
                  "resourceType": "Patient",
                  "id": "benchmark-patient",
                  "meta": { "versionId": "3", "lastUpdated": "1990-01-02T03:04:05.123+00:00" },
                  "identifier": [ {{identifiers}} ],
                  "active": true,
                  "name": [
                    { "use": "official", "family": "Chalmers", "given": [ "Peter", "James" ] },
                    { "use": "usual", "given": [ "Jim" ] },
                    { "use": "maiden", "family": "Windsor", "given": [ "Peter" ] }
                  ],
                  "telecom": [
                    { "system": "phone", "value": "(03) 5555 6473", "use": "work" },
                    { "system": "email", "value": "p.chalmers@example.org", "use": "home" }
                  ],
                  "gender": "male",
                  "birthDate": "1974-12-25",
                  "address": [
                    {
                      "use": "home",
                      "line": [ "534 Erewhon St" ],
                      "city": "PleasantVille",
                      "state": "Vic",
                      "postalCode": "3999"
                    }
                  ],
                  "managingOrganization": { "reference": "Organization/1" },
                  "generalPractitioner": [ { "reference": "Practitioner/23" } ]
                }
                """;
        }
    }
}
