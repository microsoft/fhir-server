// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Benchmarks;

/// <summary>
/// Benchmarks comparing Ignixa vs. Firely input formatters.
/// Run with: dotnet run -c Release -- --filter *InputFormatterBenchmarks*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class InputFormatterBenchmarks
{
    private byte[] _patientJsonBytes;
    private byte[] _observationJsonBytes;
    private IgnixaFhirJsonInputFormatter _ignixaFormatter;
    private FhirJsonInputFormatter _firelyFormatter;
    private ModelMetadata _resourceElementMetadata;
    private ModelMetadata _resourceMetadata;

    [GlobalSetup]
    public void Setup()
    {
        ModelExtensions.SetModelInfoProvider();

        _patientJsonBytes = Encoding.UTF8.GetBytes(Samples.GetJson("Patient"));
        _observationJsonBytes = Encoding.UTF8.GetBytes(Samples.GetJson("Weight"));

        // Set up Ignixa formatter
        var ignixaSerializer = new IgnixaJsonSerializer();
#pragma warning disable CS0618
        var firelyParser = new FhirJsonParser(new ParserSettings { PermissiveParsing = true, TruncateDateTimeToDate = true });
#pragma warning restore CS0618
        var services = new ServiceCollection();
        services.AddSingleton<IModelInfoProvider>(ModelInfoProvider.Instance);
        services.AddSingleton<IIgnixaSchemaContext>(new IgnixaSchemaContext(ModelInfoProvider.Instance));
        var sp = services.BuildServiceProvider();
        _ignixaFormatter = new IgnixaFhirJsonInputFormatter(ignixaSerializer, firelyParser, sp);

        // Set up Firely formatter
        _firelyFormatter = new FhirJsonInputFormatter(firelyParser, System.Buffers.ArrayPool<char>.Shared);

        // Metadata
        _resourceElementMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(ResourceElement));
        _resourceMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(Resource));
    }

    [Benchmark(Baseline = true, Description = "Firely formatter: Parse Patient → Resource")]
    public async Task<InputFormatterResult> Firely_ParsePatient_AsResource()
    {
        return await ReadWith(_firelyFormatter, _patientJsonBytes, _resourceMetadata);
    }

    [Benchmark(Description = "Ignixa formatter: Parse Patient → ResourceElement")]
    public async Task<InputFormatterResult> Ignixa_ParsePatient_AsResourceElement()
    {
        return await ReadWith(_ignixaFormatter, _patientJsonBytes, _resourceElementMetadata);
    }

    [Benchmark(Description = "Ignixa formatter: Parse Patient → Resource (double-parse path)")]
    public async Task<InputFormatterResult> Ignixa_ParsePatient_AsResource()
    {
        return await ReadWith(_ignixaFormatter, _patientJsonBytes, _resourceMetadata);
    }

    [Benchmark(Description = "Ignixa formatter: Parse Observation → ResourceElement")]
    public async Task<InputFormatterResult> Ignixa_ParseObservation_AsResourceElement()
    {
        return await ReadWith(_ignixaFormatter, _observationJsonBytes, _resourceElementMetadata);
    }

    private static async Task<InputFormatterResult> ReadWith(
        TextInputFormatter formatter,
        byte[] jsonBytes,
        ModelMetadata metadata)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/fhir+json";
        httpContext.Request.Body = new MemoryStream(jsonBytes);

        var context = new InputFormatterContext(
            httpContext,
            "resource",
            new ModelStateDictionary(),
            metadata,
            (stream, encoding) => new StreamReader(stream, encoding));

        return await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);
    }
}
