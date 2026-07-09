// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class IgnixaResourceValidatorTests
{
    private readonly IgnixaJsonSerializer _serializer = new IgnixaJsonSerializer();
    private readonly IIgnixaSchemaContext _schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
    private readonly IgnixaResourceValidator _validator;

    private const string ObservationWithInvalidDateTimeJson = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "29463-7",
                "display": "Body Weight"
              }
            ]
          },
          "effectiveDateTime": "2021-10-13+02:00",
          "valueQuantity": {
            "value": 185,
            "unit": "lbs",
            "system": "http://unitsofmeasure.org",
            "code": "[lb_av]"
          }
        }
        """;

    private const string ObservationWithValidDateTimeWithoutOffsetJson = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "29463-7",
                "display": "Body Weight"
              }
            ]
          },
          "effectiveDateTime": "1980-05-11T16:32:15",
          "valueQuantity": {
            "value": 185,
            "unit": "lbs",
            "system": "http://unitsofmeasure.org",
            "code": "[lb_av]"
          }
        }
        """;

    public IgnixaResourceValidatorTests()
    {
        _validator = CreateValidator(FhirSdkMode.Hybrid);
    }

    [Fact]
    public async Task GivenIgnixaResourceWithInvalidDateTime_WhenValidating_ThenInvalidShouldBeReturned()
    {
        // Arrange
        var resource = await CreateResourceElement(ObservationWithInvalidDateTimeJson);
        var results = new List<ValidationResult>();

        // Act
        var isValid = _validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, x => x.ErrorMessage.Contains("format"));
    }

    [Fact]
    public async Task GivenIgnixaResourceWithValidDateTimeWithoutOffset_WhenValidating_ThenValidShouldBeReturned()
    {
        // Arrange
        var resource = await CreateResourceElement(ObservationWithValidDateTimeWithoutOffsetJson);
        var results = new List<ValidationResult>();

        // Act
        var isValid = _validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GivenIgnixaModeAndOrdinaryResource_WhenValidationFallsBackToFirely_ThenInvalidOperationExceptionIsThrown()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Ignixa);
        var resource = await CreateResourceElement(ObservationWithValidDateTimeWithoutOffsetJson);
        var results = new List<ValidationResult>();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => validator.TryValidate(resource, results, recurse: false));
        Assert.Contains("Firely fallback is not allowed in Ignixa SDK mode.", exception.Message);
    }

    [Fact]
    public void GivenIgnixaModeAndFirelyBackedResource_WhenValidating_ThenInvalidOperationExceptionIsThrown()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Ignixa);
        var resource = new Patient { Id = "firely-backed" }.ToResourceElement();
        var results = new List<ValidationResult>();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => validator.TryValidate(resource, results, recurse: false));
        Assert.Contains("Firely fallback is not allowed in Ignixa SDK mode.", exception.Message);
    }

    [Fact]
    public void GivenHybridModeAndFirelyBackedResource_WhenValidating_ThenFallbackIsPermitted()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Hybrid);
        var resource = new Patient { Id = "firely-backed-hybrid" }.ToResourceElement();
        var results = new List<ValidationResult>();

        // Act
        var isValid = validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GivenIgnixaResourceWithInvalidDateTime_WhenValidatingCreate_ThenInvalidShouldBeReturned()
    {
        // Arrange
        var resource = await CreateResourceElement(ObservationWithInvalidDateTimeJson);
        var validator = CreateCreateResourceValidator();

        // Act
        var result = validator.Validate(new CreateResourceRequest(resource, bundleResourceContext: null));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("format"));
    }

    [Theory]
    [InlineData("StructureDefinition")]
    [InlineData("SearchParameter")]
    [InlineData("ValueSet")]
    [InlineData("CodeSystem")]
    public async Task GivenIgnixaModeAndConformanceResource_WhenValidating_ThenFirelyFallbackIsNotUsed(string resourceType)
    {
        // Arrange
        var validator = CreateIgnixaModeValidator();
        var resource = await CreateResourceElement(GetMinimalConformanceJson(resourceType));
        var results = new List<ValidationResult>();

        // Act
        var isValid = validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.True(isValid, string.Join("; ", results.Select(x => x.ErrorMessage)));
    }

    [Fact]
    public async Task GivenIgnixaModeAndInvalidConformanceResource_WhenValidating_ThenIgnixaValidationResultIsReturned()
    {
        // Arrange
        var validator = CreateIgnixaModeValidator();
        var resource = await CreateResourceElement("{\"resourceType\":\"SearchParameter\",\"url\":\"http://example.org/sp\",\"name\":\"X\",\"status\":\"active\",\"code\":\"x\",\"base\":[\"Patient\"],\"type\":\"string\",\"expression\":\"Patient.name\"}");
        var results = new List<ValidationResult>();

        // Act
        var isValid = validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, x => x.ErrorMessage.Contains("SearchParameter.description"));
    }

    [Fact]
    public async Task GivenHybridModeAndConformanceResource_WhenValidating_ThenFallbackIsPermitted()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Hybrid);
        var resource = await CreateResourceElement(GetMinimalCapabilityStatementJson());
        var results = new List<ValidationResult>();

        // Act
        var isValid = validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

    private CreateResourceValidator CreateCreateResourceValidator()
    {
        var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        contextAccessor.RequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());

        return new CreateResourceValidator(
            _validator,
            new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance, Options.Create(new CoreFeatureConfiguration())),
            Substitute.For<IProfileValidator>(),
            contextAccessor,
            Options.Create(new CoreFeatureConfiguration()));
    }

    private async Task<ResourceElement> CreateResourceElement(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        ResourceJsonNode resourceNode = await _serializer.ParseAsync(stream);
        var ignixaElement = new IgnixaResourceElement(resourceNode, _schemaContext.Schema);

        return ignixaElement.ToResourceElement();
    }

    private static string GetMinimalCapabilityStatementJson()
    {
        return ModelInfoProvider.Instance.Version switch
        {
            FhirSpecification.Stu3 => "{\"resourceType\":\"CapabilityStatement\",\"status\":\"active\",\"date\":\"2020-01-01\",\"kind\":\"instance\",\"fhirVersion\":\"3.0.2\",\"acceptUnknown\":\"both\",\"format\":[\"json\"]}",
            FhirSpecification.R5 => "{\"resourceType\":\"CapabilityStatement\",\"status\":\"active\",\"date\":\"2020-01-01\",\"kind\":\"instance\",\"fhirVersion\":\"5.0.0\",\"format\":[\"json\"]}",
            _ => "{\"resourceType\":\"CapabilityStatement\",\"status\":\"active\",\"date\":\"2020-01-01\",\"kind\":\"instance\",\"fhirVersion\":\"4.0.1\",\"format\":[\"json\"]}",
        };
    }

    private static string GetMinimalConformanceJson(string resourceType)
    {
        return resourceType switch
        {
            "ValueSet" => "{\"resourceType\":\"ValueSet\",\"url\":\"http://example.org/vs\",\"status\":\"active\"}",
            "CodeSystem" => "{\"resourceType\":\"CodeSystem\",\"url\":\"http://example.org/cs\",\"status\":\"active\",\"content\":\"complete\"}",
            "SearchParameter" => "{\"resourceType\":\"SearchParameter\",\"url\":\"http://example.org/sp\",\"name\":\"X\",\"status\":\"active\",\"description\":\"Example\",\"code\":\"x\",\"base\":[\"Patient\"],\"type\":\"string\",\"expression\":\"Patient.name\"}",
            "StructureDefinition" => "{\"resourceType\":\"StructureDefinition\",\"url\":\"http://example.org/sd\",\"status\":\"active\",\"name\":\"Example\",\"kind\":\"resource\",\"abstract\":false,\"type\":\"Patient\",\"baseDefinition\":\"http://hl7.org/fhir/StructureDefinition/Patient\",\"derivation\":\"constraint\"}",
            _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, null),
        };
    }

    private IgnixaResourceValidator CreateIgnixaModeValidator()
    {
        return CreateValidator(FhirSdkMode.Ignixa);
    }

    private IgnixaResourceValidator CreateValidator(FhirSdkMode mode)
    {
        var guard = new SdkFallbackGuard(
            new SdkModeProvider(new SdkConfiguration { Mode = mode }),
            NullLogger<SdkFallbackGuard>.Instance);

        return new IgnixaResourceValidator(_schemaContext, new ModelAttributeValidator(), guard);
    }
}
