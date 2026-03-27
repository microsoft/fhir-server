// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
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
public class IgnixaFhirJsonInputFormatterTests
{
    private readonly IgnixaFhirJsonInputFormatter _formatter;

    public IgnixaFhirJsonInputFormatterTests()
    {
        var ignixaSerializer = new IgnixaJsonSerializer();

#pragma warning disable CS0618 // Type or member is obsolete
        var firelyParser = new FhirJsonParser(new ParserSettings { PermissiveParsing = true, TruncateDateTimeToDate = true });
#pragma warning restore CS0618 // Type or member is obsolete

        var services = new ServiceCollection();
        services.AddSingleton<IModelInfoProvider>(ModelInfoProvider.Instance);
        services.AddSingleton<IIgnixaSchemaContext>(new IgnixaSchemaContext(ModelInfoProvider.Instance));
        var serviceProvider = services.BuildServiceProvider();

        _formatter = new IgnixaFhirJsonInputFormatter(ignixaSerializer, firelyParser, serviceProvider);
    }

    // ------------------------------------------------------------------
    // CanReadType
    // ------------------------------------------------------------------

    [Fact]
    public void GivenResourceType_WhenCheckingCanRead_ThenTrueIsReturned()
    {
        Assert.True(CanRead(typeof(Resource)));
    }

    [Fact]
    public void GivenResourceElementType_WhenCheckingCanRead_ThenTrueIsReturned()
    {
        Assert.True(CanRead(typeof(ResourceElement)));
    }

    [Fact]
    public void GivenResourceJsonNodeType_WhenCheckingCanRead_ThenTrueIsReturned()
    {
        Assert.True(CanRead(typeof(ResourceJsonNode)));
    }

    [Fact]
    public void GivenIgnixaResourceElementType_WhenCheckingCanRead_ThenTrueIsReturned()
    {
        Assert.True(CanRead(typeof(IgnixaResourceElement)));
    }

    [Fact]
    public void GivenJObjectType_WhenCheckingCanRead_ThenFalseIsReturned()
    {
        Assert.False(CanRead(typeof(JObject)));
    }

    [Fact]
    public void GivenStringType_WhenCheckingCanRead_ThenFalseIsReturned()
    {
        Assert.False(CanRead(typeof(string)));
    }

    // ------------------------------------------------------------------
    // ReadRequestBody — ResourceElement target (primary ingest path)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenValidPatientJson_WhenReadAsResourceElement_ThenModelIsSet()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Patient"), typeof(ResourceElement));

        // Assert
        Assert.True(result.IsModelSet);
        var resourceElement = Assert.IsType<ResourceElement>(result.Model);
        Assert.Equal("Patient", resourceElement.InstanceType);
        Assert.NotNull(resourceElement.Id);
    }

    [Fact]
    public async Task GivenValidObservationJson_WhenReadAsResourceElement_ThenCorrectTypeIsReturned()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Weight"), typeof(ResourceElement));

        // Assert
        Assert.True(result.IsModelSet);
        var resourceElement = Assert.IsType<ResourceElement>(result.Model);
        Assert.Equal("Observation", resourceElement.InstanceType);
    }

    [Fact]
    public async Task GivenValidPatientJson_WhenReadAsResourceElement_ThenIgnixaNodeIsPreserved()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Patient"), typeof(ResourceElement));

        // Assert — the ResourceElement should carry an Ignixa node for downstream fast-path serialization
        Assert.True(result.IsModelSet);
        var resourceElement = (ResourceElement)result.Model;
        var ignixaNode = resourceElement.GetIgnixaNode();
        Assert.NotNull(ignixaNode);
        Assert.Equal("Patient", ignixaNode.ResourceType);
    }

    // ------------------------------------------------------------------
    // ReadRequestBody — Resource (Firely POCO) target
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenValidPatientJson_WhenReadAsResource_ThenFirelyPocoIsReturned()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Patient"), typeof(Resource));

        // Assert
        Assert.True(result.IsModelSet);
        var patient = Assert.IsAssignableFrom<Patient>(result.Model);
        Assert.NotNull(patient.Id);
    }

    [Fact]
    public async Task GivenValidObservationJson_WhenReadAsResource_ThenCorrectPocoTypeIsReturned()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Weight"), typeof(Resource));

        // Assert
        Assert.True(result.IsModelSet);
        Assert.IsAssignableFrom<Observation>(result.Model);
    }

    // ------------------------------------------------------------------
    // ReadRequestBody — ResourceJsonNode target (native Ignixa type)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenValidPatientJson_WhenReadAsResourceJsonNode_ThenNodeIsReturned()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Patient"), typeof(ResourceJsonNode));

        // Assert
        Assert.True(result.IsModelSet);
        var node = Assert.IsType<ResourceJsonNode>(result.Model);
        Assert.Equal("Patient", node.ResourceType);
    }

    // ------------------------------------------------------------------
    // ReadRequestBody — IgnixaResourceElement target
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenValidPatientJson_WhenReadAsIgnixaResourceElement_ThenElementIsReturned()
    {
        // Arrange & Act
        var result = await ReadRequestBody(Samples.GetJson("Patient"), typeof(IgnixaResourceElement));

        // Assert
        Assert.True(result.IsModelSet);
        var element = Assert.IsType<IgnixaResourceElement>(result.Model);
        Assert.Equal("Patient", element.ResourceNode.ResourceType);
    }

    // ------------------------------------------------------------------
    // Error handling
    // ------------------------------------------------------------------

    [Fact]
    public async Task GivenMalformedJson_WhenRead_ThenFailureIsReturned()
    {
        // Arrange
        var modelState = new ModelStateDictionary();

        // Act
        var result = await ReadRequestBody("{ this is not valid json }", typeof(ResourceElement), modelState);

        // Assert
        Assert.False(result.IsModelSet);
        Assert.True(modelState.ErrorCount > 0);
    }

    [Fact]
    public async Task GivenEmptyInput_WhenRead_ThenNoValueIsReturned()
    {
        // Arrange
        var modelState = new ModelStateDictionary();

        // Act
        var result = await ReadRequestBody(string.Empty, typeof(ResourceElement), modelState);

        // Assert
        Assert.False(result.IsModelSet);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool CanRead(Type modelType)
    {
        var modelMetadata = Substitute.For<ModelMetadata>(ModelMetadataIdentity.ForType(modelType));
        var defaultHttpContext = new DefaultHttpContext();
        defaultHttpContext.Request.ContentType = "application/fhir+json";

        return _formatter.CanRead(
            new InputFormatterContext(
                defaultHttpContext,
                "model",
                new ModelStateDictionary(),
                modelMetadata,
                Substitute.For<Func<Stream, Encoding, TextReader>>()));
    }

    private async Task<InputFormatterResult> ReadRequestBody(
        string json,
        Type targetType,
        ModelStateDictionary modelState = null)
    {
        modelState ??= new ModelStateDictionary();

        var metaData = new DefaultModelMetadata(
            new EmptyModelMetadataProvider(),
            Substitute.For<ICompositeMetadataDetailsProvider>(),
            new DefaultMetadataDetails(
                ModelMetadataIdentity.ForType(targetType),
                ModelAttributes.GetAttributesForType(targetType)));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/fhir+json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var context = new InputFormatterContext(
            httpContext,
            KnownActionParameterNames.Resource,
            modelState,
            metaData,
            (stream, encoding) => new StreamReader(stream, encoding));

        return await _formatter.ReadRequestBodyAsync(context, Encoding.UTF8);
    }
}
