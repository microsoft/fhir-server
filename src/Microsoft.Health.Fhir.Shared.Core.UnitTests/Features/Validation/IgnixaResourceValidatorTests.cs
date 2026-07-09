// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
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
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class IgnixaResourceValidatorTests
{
    private readonly IgnixaJsonSerializer _serializer = new IgnixaJsonSerializer();
    private readonly IIgnixaSchemaContext _schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
    private readonly IgnixaResourceValidator _validator;

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

    [Fact]
    public async Task GivenIgnixaModeAndConformanceResource_WhenValidating_ThenInvalidOperationExceptionIsThrown()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Ignixa);
        var resource = await CreateResourceElement(CapabilityStatementJson);
        var results = new List<ValidationResult>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => validator.TryValidate(resource, results, recurse: false));
    }

    [Fact]
    public async Task GivenHybridModeAndConformanceResource_WhenValidating_ThenFallbackIsPermitted()
    {
        // Arrange
        var validator = CreateValidator(FhirSdkMode.Hybrid);
        var resource = await CreateResourceElement(CapabilityStatementJson);
        var results = new List<ValidationResult>();

        // Act
        var isValid = validator.TryValidate(resource, results, recurse: false);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

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

    private const string CapabilityStatementJson = """
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "date": "2020-01-01",
          "kind": "instance",
          "fhirVersion": "4.0.1",
          "format": [
            "json"
          ]
        }
        """;

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

    private IgnixaResourceValidator CreateValidator(FhirSdkMode mode)
    {
        var guard = new SdkFallbackGuard(
            new SdkModeProvider(new SdkConfiguration { Mode = mode }),
            NullLogger<SdkFallbackGuard>.Instance);

        return new IgnixaResourceValidator(_schemaContext, new ModelAttributeValidator(), guard);
    }
}
