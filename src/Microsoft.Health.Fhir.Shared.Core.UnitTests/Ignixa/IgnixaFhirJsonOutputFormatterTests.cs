// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Serialization)]
public class IgnixaFhirJsonOutputFormatterTests
{
    private static readonly FhirJsonParser Parser = new FhirJsonParser(DefaultParserSettings.Settings);
    private readonly IgnixaFhirJsonOutputFormatter _formatter;
    private readonly IIgnixaJsonSerializer _ignixaSerializer;
    private readonly ResourceWrapperFactory _wrapperFactory;

    public IgnixaFhirJsonOutputFormatterTests()
    {
        _ignixaSerializer = new IgnixaJsonSerializer();
        var firelySerializer = new FhirJsonSerializer();
        _formatter = new IgnixaFhirJsonOutputFormatter(
            _ignixaSerializer,
            firelySerializer,
            ModelInfoProvider.Instance,
            new BundleSerializer(new FhirJsonSerializer()),
            Deserializers.ResourceDeserializer);

        var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        requestContextAccessor.RequestContext.Returns(_ => new FhirRequestContext("get", "https://localhost/Patient", "https://localhost", "correlation", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>()));

        _wrapperFactory = new ResourceWrapperFactory(
            new RawResourceFactory(new IgnixaJsonSerializer(), new FhirJsonSerializer()),
            requestContextAccessor,
            Substitute.For<ISearchIndexer>(),
            Substitute.For<IClaimsExtractor>(),
            Substitute.For<ICompartmentIndexer>(),
            Substitute.For<ISearchParameterDefinitionManager>(),
            Deserializers.ResourceDeserializer);
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

    [Theory]
    [InlineData(typeof(IgnixaResourceElement))]
    [InlineData(typeof(global::Ignixa.Serialization.SourceNodes.ResourceJsonNode))]
    public async Task GivenIgnixaResource_WhenWrittenWithElementsParameter_ThenOnlyRequestedElementsAreWritten(Type objectType)
    {
        var patient = new Patient
        {
            Id = "elements-test",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
        };
        var node = _ignixaSerializer.Parse(patient.ToJson());
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        object resource = objectType == typeof(IgnixaResourceElement)
            ? new IgnixaResourceElement(node, schemaContext.Schema)
            : node;

        var json = await WriteObject(resource, objectType, "?_elements=active");

        var parsed = Parser.Parse<Patient>(json);
        Assert.True(parsed.Active);
        Assert.Empty(parsed.Name);
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
    // WriteResponseBody — Bundle with RawBundleEntryComponent entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenABundleWithRawEntries_WhenWritten_ThenEachEntryResourceIsPopulated()
    {
        var patient = Samples.GetDefaultPatient();
        var observation = Samples.GetDefaultObservation().ToPoco();
        observation.Id = Guid.NewGuid().ToString();

        var (rawBundle, bundle) = CreateBundleWithRawEntries(patient, observation.ToResourceElement());

        var json = await WriteObject(rawBundle, typeof(Bundle));

        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal(2, parsed.Entry.Count);
        Assert.All(parsed.Entry, entry => Assert.NotNull(entry.Resource));
        Assert.True(parsed.IsExactly(bundle));
    }

    [Fact]
    public async Task GivenABundleWithRawEntries_WhenWrittenWithSummaryCount_ThenEntriesAreOmitted()
    {
        var (rawBundle, _) = CreateBundleWithRawEntries(Samples.GetDefaultPatient());

        var json = await WriteObject(rawBundle, typeof(Bundle), "?_summary=count");

        var parsed = Parser.Parse<Bundle>(json);
        Assert.Empty(parsed.Entry);
        Assert.Equal(1, parsed.Total);
    }

    [Fact]
    public async Task GivenABundleWithRawEntries_WhenWrittenWithElements_ThenOnlyRequestedElementsAreWrittenPerEntry()
    {
        var patient = new Patient
        {
            Id = "elements-bundle-test",
            Active = true,
            Name = { new HumanName { Family = "Smith", Given = new[] { "John" } } },
        };

        var (rawBundle, _) = CreateBundleWithRawEntries(patient.ToResourceElement());

        var json = await WriteObject(rawBundle, typeof(Bundle), "?_elements=active");

        var parsed = Parser.Parse<Bundle>(json);
        var entry = Assert.Single(parsed.Entry);
        var parsedPatient = Assert.IsType<Patient>(entry.Resource);
        Assert.True(parsedPatient.Active);
        Assert.Empty(parsedPatient.Name);
    }

    [Fact]
    public async Task GivenABundleWithMixedRawAndPocoEntries_WhenWritten_ThenAllEntryResourcesAreWritten()
    {
        var (rawBundle, _) = CreateBundleWithRawEntries(Samples.GetDefaultPatient());
        var operationOutcome = new OperationOutcome
        {
            Id = "search-issues",
            Issue =
            {
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Warning,
                    Code = OperationOutcome.IssueType.Incomplete,
                },
            },
        };

        rawBundle.Entry.Add(new EntryComponent { Resource = operationOutcome });

        var json = await WriteObject(rawBundle, typeof(Bundle));

        var parsed = Parser.Parse<Bundle>(json);
        Assert.Equal(2, parsed.Entry.Count);
        Assert.All(parsed.Entry, entry => Assert.NotNull(entry.Resource));
        var parsedOutcome = Assert.IsType<OperationOutcome>(parsed.Entry[1].Resource);
        Assert.Equal("search-issues", parsedOutcome.Id);
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

    private (Bundle rawBundle, Bundle bundle) CreateBundleWithRawEntries(params ResourceElement[] resources)
    {
        string id = Guid.NewGuid().ToString();
        var rawBundle = new Bundle();
        var bundle = new Bundle();

        rawBundle.Id = bundle.Id = id;
        rawBundle.Type = bundle.Type = BundleType.Searchset;
        rawBundle.Total = bundle.Total = resources.Length;

        foreach (var resource in resources)
        {
            var poco = resource.ToPoco();
            poco.VersionId = "1";
            poco.Meta.LastUpdated = DateTimeOffset.UtcNow;
            poco.Meta.Tag = new List<Coding>
            {
                new Coding { System = "testTag", Code = Guid.NewGuid().ToString() },
            };
            var wrapper = _wrapperFactory.Create(poco.ToResourceElement(), deleted: false, keepMeta: true);
            wrapper.Version = "1";

            var searchComponent = new SearchComponent { Mode = SearchEntryMode.Match };
            rawBundle.Entry.Add(new RawBundleEntryComponent(wrapper) { Search = searchComponent });
            bundle.Entry.Add(new EntryComponent { Resource = poco, Search = searchComponent });
        }

        return (rawBundle, bundle);
    }

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

    private async Task<string> WriteObject(object obj, Type objectType, string query = null)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
        httpContext.Response.Body = body;
        if (query != null)
        {
            httpContext.Request.QueryString = new QueryString(query);
        }

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
