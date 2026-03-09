// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Benchmarks;

/// <summary>
/// Benchmarks comparing Ignixa vs. Firely serialization paths.
/// Run with: dotnet run -c Release -- --filter *SerializationBenchmarks*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class SerializationBenchmarks
{
    private readonly IIgnixaJsonSerializer _ignixaSerializer = new IgnixaJsonSerializer();
    private readonly FhirJsonSerializer _firelySerializer = new FhirJsonSerializer();
    private readonly FhirJsonParser _firelyParser = new FhirJsonParser();

    private string _patientJson;
    private string _observationJson;
    private Patient _patientPoco;
    private Observation _observationPoco;
    private ResourceElement _patientResourceElement;
    private ResourceElement _observationResourceElement;
    private ResourceElement _ignixa_patientResourceElement;

    [GlobalSetup]
    public void Setup()
    {
        ModelExtensions.SetModelInfoProvider();

        _patientJson = Samples.GetJson("Patient");
        _observationJson = Samples.GetJson("Weight");
        _patientPoco = _firelyParser.Parse<Patient>(_patientJson);
        _observationPoco = _firelyParser.Parse<Observation>(_observationJson);
        _patientResourceElement = _patientPoco.ToResourceElement();
        _observationResourceElement = _observationPoco.ToResourceElement();

        // Create Ignixa-backed ResourceElement (fast path)
        var ignixaNode = _ignixaSerializer.Parse(_patientJson);
        _ignixa_patientResourceElement = new ResourceElement(
            new IgnixaResourceElement(ignixaNode, new IgnixaSchemaContext(ModelInfoProvider.Instance).Schema).ToTypedElement(),
            ignixaNode);
    }

    // ------------------------------------------------------------------
    // Parse benchmarks
    // ------------------------------------------------------------------

    [Benchmark(Description = "Firely: Parse Patient JSON")]
    public Patient Firely_ParsePatient() => _firelyParser.Parse<Patient>(_patientJson);

    [Benchmark(Description = "Ignixa: Parse Patient JSON")]
    public global::Ignixa.Serialization.SourceNodes.ResourceJsonNode Ignixa_ParsePatient() => _ignixaSerializer.Parse(_patientJson);

    [Benchmark(Description = "Firely: Parse Observation JSON")]
    public Observation Firely_ParseObservation() => _firelyParser.Parse<Observation>(_observationJson);

    [Benchmark(Description = "Ignixa: Parse Observation JSON")]
    public global::Ignixa.Serialization.SourceNodes.ResourceJsonNode Ignixa_ParseObservation() => _ignixaSerializer.Parse(_observationJson);

    // ------------------------------------------------------------------
    // Serialize benchmarks
    // ------------------------------------------------------------------

    [Benchmark(Description = "Firely: Serialize Patient POCO")]
    public string Firely_SerializePatient() => _firelySerializer.SerializeToString(_patientPoco);

    [Benchmark(Description = "Ignixa: Serialize Patient (via parse+serialize)")]
    public string Ignixa_SerializePatient()
    {
        var node = _ignixaSerializer.Parse(_patientJson);
        return _ignixaSerializer.Serialize(node);
    }

    // ------------------------------------------------------------------
    // RawResourceFactory benchmarks
    // ------------------------------------------------------------------

    [Benchmark(Description = "RawResourceFactory: Firely-sourced ResourceElement (fallback path)")]
    public RawResource RawResourceFactory_FirelyFallback()
    {
        var factory = new RawResourceFactory(_ignixaSerializer, _firelySerializer);
        return factory.Create(_patientResourceElement, keepMeta: false);
    }

    [Benchmark(Description = "RawResourceFactory: Ignixa-sourced ResourceElement (fast path)")]
    public RawResource RawResourceFactory_IgnixaFastPath()
    {
        var factory = new RawResourceFactory(_ignixaSerializer, _firelySerializer);
        return factory.Create(_ignixa_patientResourceElement, keepMeta: false);
    }

    // ------------------------------------------------------------------
    // NDJSON serializer benchmarks
    // ------------------------------------------------------------------

    [Benchmark(Description = "NdjsonSerializer: Firely-sourced ResourceElement")]
    public byte[] NdjsonSerializer_FirelySource()
    {
        var serializer = new ResourceToNdjsonBytesSerializer(_ignixaSerializer);
        return serializer.Serialize(_patientResourceElement);
    }

    [Benchmark(Description = "NdjsonSerializer: Ignixa-sourced ResourceElement")]
    public byte[] NdjsonSerializer_IgnixaSource()
    {
        var serializer = new ResourceToNdjsonBytesSerializer(_ignixaSerializer);
        return serializer.Serialize(_ignixa_patientResourceElement);
    }

    // ------------------------------------------------------------------
    // Full round-trip benchmarks
    // ------------------------------------------------------------------

    [Benchmark(Description = "Firely: Full round-trip (parse → serialize)")]
    public string Firely_FullRoundTrip()
    {
        var resource = _firelyParser.Parse<Patient>(_patientJson);
        return _firelySerializer.SerializeToString(resource);
    }

    [Benchmark(Description = "Ignixa: Full round-trip (parse → serialize)")]
    public string Ignixa_FullRoundTrip()
    {
        var node = _ignixaSerializer.Parse(_patientJson);
        return _ignixaSerializer.Serialize(node);
    }

    [Benchmark(Description = "Triple-hop: Firely serialize → Ignixa parse → Ignixa serialize")]
    public string TripleHop_FirelyToIgnixa()
    {
        var json = _firelySerializer.SerializeToString(_patientPoco);
        var node = _ignixaSerializer.Parse(json);
        return _ignixaSerializer.Serialize(node);
    }
}
