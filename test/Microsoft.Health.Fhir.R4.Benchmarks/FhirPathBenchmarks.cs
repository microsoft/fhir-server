// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Benchmarks;

/// <summary>
/// Benchmarks comparing Ignixa FHIRPath evaluation vs. Firely FHIRPath.
/// Run with: dotnet run -c Release -- --filter *FhirPathBenchmarks*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class FhirPathBenchmarks
{
    private static readonly string[] Expressions =
    [
        "Patient.name",
        "Patient.name.family",
        "Patient.identifier",
        "Patient.active",
        "Patient.telecom.where(system='phone')",
    ];

    private IFhirPathProvider _firelyProvider;
    private IFhirPathProvider _ignixaProvider;
    private ITypedElement _firelyElement;
    private ITypedElement _ignixaElement;

    [GlobalSetup]
    public void Setup()
    {
        ModelExtensions.SetModelInfoProvider();

        var patientJson = Samples.GetJson("Patient");

        // Firely path: parse to POCO, then get ITypedElement
        var parser = new FhirJsonParser();
        var poco = parser.Parse<Patient>(patientJson);
        _firelyElement = poco.ToTypedElement();
        _firelyProvider = new FirelyFhirPathProvider();

        // Ignixa path: parse to ResourceJsonNode, wrap in IgnixaResourceElement, get ITypedElement
        var ignixaSerializer = new IgnixaJsonSerializer();
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        var resourceNode = ignixaSerializer.Parse(patientJson);
        var ignixaResourceElement = new IgnixaResourceElement(resourceNode, schemaContext.Schema);
        _ignixaElement = ignixaResourceElement.ToTypedElement();
        _ignixaProvider = new IgnixaFhirPathProvider(schemaContext.Schema);

        // Warm caches
        foreach (var expr in Expressions)
        {
            _firelyProvider.Compile(expr);
            _ignixaProvider.Compile(expr);
        }
    }

    [Benchmark(Baseline = true, Description = "Firely: Compile + Evaluate 5 expressions")]
    public int Firely_EvaluateAll()
    {
        int total = 0;
        foreach (var expr in Expressions)
        {
            var compiled = _firelyProvider.Compile(expr);
            total += compiled.Evaluate(_firelyElement).Count();
        }

        return total;
    }

    [Benchmark(Description = "Ignixa: Compile + Evaluate 5 expressions")]
    public int Ignixa_EvaluateAll()
    {
        int total = 0;
        foreach (var expr in Expressions)
        {
            var compiled = _ignixaProvider.Compile(expr);
            total += compiled.Evaluate(_ignixaElement).Count();
        }

        return total;
    }

    [Benchmark(Description = "Firely: Scalar (Patient.name.family)")]
    public string Firely_Scalar() => _firelyProvider.Scalar<string>(_firelyElement, "Patient.name.family");

    [Benchmark(Description = "Ignixa: Scalar (Patient.name.family)")]
    public string Ignixa_Scalar() => _ignixaProvider.Scalar<string>(_ignixaElement, "Patient.name.family");

    [Benchmark(Description = "Firely: Predicate (Patient.active)")]
    public bool Firely_Predicate() => _firelyProvider.Predicate(_firelyElement, "Patient.active");

    [Benchmark(Description = "Ignixa: Predicate (Patient.active)")]
    public bool Ignixa_Predicate() => _ignixaProvider.Predicate(_ignixaElement, "Patient.active");
}
