// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

/// <summary>
/// Unit tests for <see cref="IgnixaFhirPathProvider"/> — validates FHIRPath compilation,
/// evaluation, scalar extraction, and predicate evaluation using the Ignixa engine.
/// </summary>
[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class IgnixaFhirPathProviderTests
{
    private readonly IFhirPathProvider _provider;
    private readonly IIgnixaJsonSerializer _serializer;
    private readonly global::Ignixa.Abstractions.ISchema _schema;

    public IgnixaFhirPathProviderTests()
    {
        ModelExtensions.SetModelInfoProvider();

        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        _schema = schemaContext.Schema;
        _serializer = new IgnixaJsonSerializer();
        _provider = new IgnixaFhirPathProvider(_schema);
    }

    // ------------------------------------------------------------------
    // Compile
    // ------------------------------------------------------------------

    [Fact]
    public void GivenASimpleExpression_WhenCompiled_ThenReturnsCompiledExpression()
    {
        // Arrange
        const string expression = "Patient.name";

        // Act
        var compiled = _provider.Compile(expression);

        // Assert
        Assert.NotNull(compiled);
        Assert.Equal(expression, compiled.Expression);
    }

    [Fact]
    public void GivenTheSameExpression_WhenCompiledMultipleTimes_ThenReturnsCachedInstance()
    {
        // Arrange
        const string expression = "Patient.name";

        // Act
        var first = _provider.Compile(expression);
        var second = _provider.Compile(expression);

        // Assert
        Assert.Same(first, second);
    }

    // ------------------------------------------------------------------
    // Evaluate — using Firely ITypedElement input (compatibility path)
    // ------------------------------------------------------------------

    [Fact]
    public void GivenAPatientResource_WhenEvaluatingNameExpression_ThenReturnsNameElements()
    {
        // Arrange
        var patient = new Patient
        {
            Name =
            {
                new HumanName { Family = "Smith", Given = new[] { "John" } },
            },
        };
        var element = patient.ToTypedElement();

        // Act
        var results = _provider.Evaluate(element, "Patient.name").ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("HumanName", results[0].InstanceType);
    }

    [Fact]
    public void GivenAPatientResource_WhenEvaluatingFamilyExpression_ThenReturnsStringValue()
    {
        // Arrange
        var patient = new Patient
        {
            Name =
            {
                new HumanName { Family = "Smith" },
            },
        };
        var element = patient.ToTypedElement();

        // Act
        var result = _provider.Scalar<string>(element, "Patient.name.family");

        // Assert
        Assert.Equal("Smith", result);
    }

    [Fact]
    public void GivenAPatientResource_WhenEvaluatingActiveExpression_ThenReturnsBoolValue()
    {
        // Arrange
        var patient = new Patient { Active = true };
        var element = patient.ToTypedElement();

        // Act
        var result = _provider.Predicate(element, "Patient.active");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GivenAnInactivePatient_WhenEvaluatingActiveExpression_ThenReturnsFalse()
    {
        // Arrange
        var patient = new Patient { Active = false };
        var element = patient.ToTypedElement();

        // Act
        var result = _provider.Predicate(element, "Patient.active");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenAPatientWithNoName_WhenEvaluatingNameExists_ThenReturnsFalse()
    {
        // Arrange
        var patient = new Patient();
        var element = patient.ToTypedElement();

        // Act
        var result = _provider.Predicate(element, "Patient.name.exists()");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GivenAPatientWithName_WhenEvaluatingNameExists_ThenReturnsTrue()
    {
        // Arrange
        var patient = new Patient
        {
            Name = { new HumanName { Family = "Smith" } },
        };
        var element = patient.ToTypedElement();

        // Act
        var result = _provider.Predicate(element, "Patient.name.exists()");

        // Assert
        Assert.True(result);
    }

    // ------------------------------------------------------------------
    // Evaluate — using Ignixa-parsed ITypedElement (primary path)
    // ------------------------------------------------------------------

    [Fact]
    public void GivenAnIgnixaParsedPatient_WhenEvaluatingNameFamily_ThenReturnsCorrectValue()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _serializer.Parse(patientJson);
        var ignixaElement = new IgnixaResourceElement(node, _schema);
        var typedElement = ignixaElement.ToTypedElement();

        // Act
        var result = _provider.Scalar<string>(typedElement, "Patient.name.family");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GivenAnIgnixaParsedPatient_WhenEvaluatingId_ThenReturnsCorrectValue()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _serializer.Parse(patientJson);
        var ignixaElement = new IgnixaResourceElement(node, _schema);
        var typedElement = ignixaElement.ToTypedElement();

        // Act
        var result = _provider.Scalar<string>(typedElement, "Patient.id");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GivenAnIgnixaParsedPatient_WhenEvaluatingMultipleExpressions_ThenAllReturnResults()
    {
        // Arrange
        var patientJson = Samples.GetJson("Patient");
        var node = _serializer.Parse(patientJson);
        var ignixaElement = new IgnixaResourceElement(node, _schema);
        var typedElement = ignixaElement.ToTypedElement();

        string[] expressions =
        {
            "Patient.name",
            "Patient.name.family",
            "Patient.identifier",
        };

        // Act & Assert
        foreach (var expr in expressions)
        {
            var compiled = _provider.Compile(expr);
            var results = compiled.Evaluate(typedElement).ToList();
            Assert.NotEmpty(results);
        }
    }

    // ------------------------------------------------------------------
    // Where clause / filter expressions
    // ------------------------------------------------------------------

    [Fact]
    public void GivenAPatientWithMultipleIdentifiers_WhenFilteringBySystem_ThenReturnsFilteredResults()
    {
        // Arrange
        var patient = new Patient
        {
            Identifier =
            {
                new Identifier("http://example.org", "12345"),
                new Identifier("http://other.org", "67890"),
            },
        };
        var element = patient.ToTypedElement();

        // Act
        var results = _provider.Evaluate(element, "Patient.identifier.where(system='http://example.org')").ToList();

        // Assert
        Assert.Single(results);
    }

    // ------------------------------------------------------------------
    // ICompiledFhirPath direct usage
    // ------------------------------------------------------------------

    [Fact]
    public void GivenACompiledExpression_WhenEvaluatedMultipleTimes_ThenProducesConsistentResults()
    {
        // Arrange
        var patient = new Patient
        {
            Name = { new HumanName { Family = "Jones" } },
        };
        var element = patient.ToTypedElement();
        var compiled = _provider.Compile("Patient.name.family");

        // Act
        var result1 = compiled.Scalar<string>(element);
        var result2 = compiled.Scalar<string>(element);

        // Assert
        Assert.Equal("Jones", result1);
        Assert.Equal(result1, result2);
    }
}
