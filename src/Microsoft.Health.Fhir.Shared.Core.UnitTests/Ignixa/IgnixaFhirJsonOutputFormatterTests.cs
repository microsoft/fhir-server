// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Serialization)]
public class IgnixaFhirJsonOutputFormatterTests
{
    private static readonly FhirJsonParser Parser = new FhirJsonParser();
    private readonly IgnixaFhirJsonOutputFormatter _formatter;
    private readonly IIgnixaJsonSerializer _ignixaSerializer;

    public IgnixaFhirJsonOutputFormatterTests()
    {
        _ignixaSerializer = new IgnixaJsonSerializer();
        var firelySerializer = new FhirJsonSerializer();
        _formatter = new IgnixaFhirJsonOutputFormatter(_ignixaSerializer, firelySerializer);
    }

    // ------------------------------------------------------------------
    // CanWriteType
    // ------------------------------------------------------------------

    [Fact]
    public void GivenResourceType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(Resource)));
    }

    [Fact]
    public void GivenObservationType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(Observation)));
    }

    [Fact]
    public void GivenRawResourceElementType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(RawResourceElement)));
    }

    [Fact]
    public void GivenJObjectType_WhenCheckingCanWrite_ThenFalseIsReturned()
    {
        Assert.False(CanWrite(typeof(JObject)));
    }

    [Fact]
    public void GivenStringType_WhenCheckingCanWrite_ThenFalseIsReturned()
    {
        Assert.False(CanWrite(typeof(string)));
    }

    [Fact]
    public void GivenResourceJsonNodeType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode)));
    }

    [Fact]
    public void GivenIgnixaResourceElementType_WhenCheckingCanWrite_ThenTrueIsReturned()
    {
        Assert.True(CanWrite(typeof(IgnixaResourceElement)));
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — ResourceJsonNode (native Ignixa type)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAResourceJsonNode_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _ignixaSerializer.Parse(patientJson);

        // Act
        var json = await WriteObject(node, typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode));

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.NotNull(parsed.Id);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — IgnixaResourceElement
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAnIgnixaResourceElement_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _ignixaSerializer.Parse(patientJson);
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        var element = new IgnixaResourceElement(node, schemaContext.Schema);

        // Act
        var json = await WriteObject(element, typeof(IgnixaResourceElement));

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.NotNull(parsed.Id);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — Firely Resource POCO
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAFirelyPatient_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-123",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
        };

        // Act
        var json = await WriteResource(patient);

        // Assert — the output should be parseable by Firely and structurally equivalent
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("test-123", parsed.Id);
        Assert.Equal("Smith", parsed.Name[0].Family);
        Assert.Equal("John", parsed.Name[0].Given.First());
        Assert.Equal(true, parsed.Active);
    }

    [Fact]
    public async Task GivenAFirelyObservation_WhenWritten_ThenValidJsonIsProduced()
    {
        // Arrange
        var observation = Samples.GetDefaultObservation().ToPoco<Observation>();

        // Act
        var json = await WriteResource(observation);

        // Assert
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Observation>(json);
        Assert.Equal(observation.Id, parsed.Id);
    }

    // ------------------------------------------------------------------
    // WriteResponseBody — RawResourceElement (zero-copy path)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenARawResourceElement_WhenWritten_ThenRawJsonIsPassedThrough()
    {
        // Arrange
        var patient = new Patient { Id = "raw-test" };
        var rawJson = new FhirJsonSerializer().SerializeToString(patient);
        var wrapper = new ResourceWrapper(
            patient.ToResourceElement(),
            new RawResource(rawJson, FhirResourceFormat.Json, isMetaSet: true),
            null,
            false,
            null,
            null,
            null);
        var rawElement = new RawResourceElement(wrapper);

        // Act
        var json = await WriteRawResourceElement(rawElement);

        // Assert — the raw JSON should be written directly
        Assert.False(string.IsNullOrEmpty(json));
        var parsed = Parser.Parse<Patient>(json);
        Assert.Equal("raw-test", parsed.Id);
    }

    // ------------------------------------------------------------------
    // Pretty printing
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenAResource_WhenWrittenWithPrettyTrue_ThenOutputIsIndented()
    {
        // Arrange
        var patient = new Patient { Id = "pretty-test" };

        // Act
        var json = await WriteResource(patient, prettyQuery: "?_pretty=true");

        // Assert — indented JSON will contain newlines
        Assert.Contains("\n", json);
    }

    [Fact]
    public async Task GivenAResource_WhenWrittenWithoutPretty_ThenOutputIsCompact()
    {
        // Arrange
        var patient = new Patient { Id = "compact-test" };

        // Act
        var json = await WriteResource(patient);

        // Assert — compact JSON should not contain indentation newlines between properties
        Assert.DoesNotContain("\n  ", json);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool CanWrite(Type modelType)
    {
        var defaultHttpContext = new DefaultHttpContext();
        defaultHttpContext.Request.ContentType = "application/fhir+json";

        return _formatter.CanWriteResult(
            new OutputFormatterWriteContext(
                defaultHttpContext,
                Substitute.For<Func<Stream, Encoding, TextWriter>>(),
                modelType,
                null));
    }

    private async Task<string> WriteResource(Resource resource, string prettyQuery = null)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
        httpContext.Response.Body = body;

        if (prettyQuery != null)
        {
            httpContext.Request.QueryString = new QueryString(prettyQuery);
        }

        using var writer = new StringWriter();
        var writeContext = new OutputFormatterWriteContext(
            httpContext,
            (_, _) => writer,
            resource.GetType(),
            resource);

        await _formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

        body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private async Task<string> WriteRawResourceElement(RawResourceElement rawElement)
    {
        return await WriteObject(rawElement, typeof(RawResourceElement));
    }

    private async Task<string> WriteObject(object obj, Type objectType)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
        httpContext.Response.Body = body;

        using var writer = new StringWriter();
        var writeContext = new OutputFormatterWriteContext(
            httpContext,
            (_, _) => writer,
            objectType,
            obj);

        await _formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

        body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }
}
