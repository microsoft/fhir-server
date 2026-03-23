// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests;

/// <summary>
/// Smoke tests that verify the ViewDefinitionEvaluator can bridge Firely SDK resources
/// through the Ignixa IElement adapter and produce correct ViewDefinition output.
/// </summary>
public class ViewDefinitionEvaluatorTests
{
    private readonly IViewDefinitionEvaluator _evaluator;

    public ViewDefinitionEvaluatorTests()
    {
        _evaluator = new ViewDefinitionEvaluator(
            NullLogger<ViewDefinitionEvaluator>.Instance);
    }

    [Fact]
    public void GivenAPatientResource_WhenEvaluatingPatientDemographicsView_ThenColumnsAreCorrect()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-patient-1",
            BirthDate = "1990-03-15",
            Gender = AdministrativeGender.Female,
            Name =
            {
                new HumanName
                {
                    Use = HumanName.NameUse.Official,
                    Family = "Smith",
                    Given = new[] { "Jane" },
                },
            },
        };

        ResourceElement resourceElement = ToResourceElement(patient);

        string viewDefinitionJson = """
            {
                "name": "patient_demographics",
                "resource": "Patient",
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" },
                            { "name": "gender", "path": "gender" },
                            { "name": "birth_date", "path": "birthDate" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resourceElement);

        // Assert
        Assert.Equal("patient_demographics", result.ViewDefinitionName);
        Assert.Equal("Patient", result.ResourceType);
        Assert.Single(result.Rows);

        ViewDefinitionRow row = result.Rows[0];
        Assert.Equal("test-patient-1", row["id"]?.ToString());
        Assert.Equal("female", row["gender"]?.ToString());
        Assert.NotNull(row["birth_date"]);
    }

    [Fact]
    public void GivenAPatientWithMultipleNames_WhenEvaluatingForEachView_ThenMultipleRowsProduced()
    {
        // Arrange
        var patient = new Patient
        {
            Id = "test-patient-2",
            Name =
            {
                new HumanName { Use = HumanName.NameUse.Official, Family = "Johnson" },
                new HumanName { Use = HumanName.NameUse.Maiden, Family = "Williams" },
            },
        };

        ResourceElement resourceElement = ToResourceElement(patient);

        string viewDefinitionJson = """
            {
                "name": "patient_names",
                "resource": "Patient",
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" }
                        ]
                    },
                    {
                        "forEach": "name",
                        "column": [
                            { "name": "family", "path": "family" },
                            { "name": "name_use", "path": "use" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resourceElement);

        // Assert
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal("test-patient-2", r["id"]?.ToString()));

        var families = result.Rows.Select(r => r["family"]?.ToString()).OrderBy(f => f).ToList();
        Assert.Contains("Johnson", families);
        Assert.Contains("Williams", families);
    }

    [Fact]
    public void GivenANonMatchingResource_WhenEvaluatingWithWhereFilter_ThenZeroRowsReturned()
    {
        // Arrange - Patient with active=false
        var patient = new Patient
        {
            Id = "inactive-patient",
            Active = false,
        };

        ResourceElement resourceElement = ToResourceElement(patient);

        string viewDefinitionJson = """
            {
                "name": "active_patients",
                "resource": "Patient",
                "where": [
                    { "path": "active = true" }
                ],
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resourceElement);

        // Assert
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void GivenAMatchingResource_WhenEvaluatingWithWhereFilter_ThenRowIsReturned()
    {
        // Arrange - Patient with active=true
        var patient = new Patient
        {
            Id = "active-patient",
            Active = true,
        };

        ResourceElement resourceElement = ToResourceElement(patient);

        string viewDefinitionJson = """
            {
                "name": "active_patients",
                "resource": "Patient",
                "where": [
                    { "path": "active = true" }
                ],
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resourceElement);

        // Assert
        Assert.Single(result.Rows);
        Assert.Equal("active-patient", result.Rows[0]["id"]?.ToString());
    }

    [Fact]
    public void GivenMultipleResources_WhenEvaluatingMany_ThenRowsFromAllResourcesReturned()
    {
        // Arrange
        var patients = new[]
        {
            new Patient { Id = "p1", Gender = AdministrativeGender.Male },
            new Patient { Id = "p2", Gender = AdministrativeGender.Female },
            new Patient { Id = "p3", Gender = AdministrativeGender.Other },
        };

        IEnumerable<ResourceElement> resourceElements = patients.Select(ToResourceElement);

        string viewDefinitionJson = """
            {
                "name": "patient_genders",
                "resource": "Patient",
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" },
                            { "name": "gender", "path": "gender" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.EvaluateMany(viewDefinitionJson, resourceElements);

        // Assert
        Assert.Equal(3, result.Rows.Count);
        var ids = result.Rows.Select(r => r["id"]?.ToString()).OrderBy(id => id).ToList();
        Assert.Equal(new[] { "p1", "p2", "p3" }, ids);
    }

    [Fact]
    public void GivenABloodPressureObservation_WhenEvaluatingBPView_ThenComponentValuesExtracted()
    {
        // Arrange - Blood Pressure Observation with systolic and diastolic components
        var observation = new Observation
        {
            Id = "bp-1",
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "85354-9", "Blood pressure panel"),
            Subject = new ResourceReference("Patient/test-patient-1"),
            Effective = new FhirDateTime("2024-01-15T10:30:00Z"),
            Component =
            {
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8480-6", "Systolic BP"),
                    Value = new Quantity(120, "mmHg", "http://unitsofmeasure.org"),
                },
                new Observation.ComponentComponent
                {
                    Code = new CodeableConcept("http://loinc.org", "8462-4", "Diastolic BP"),
                    Value = new Quantity(80, "mmHg", "http://unitsofmeasure.org"),
                },
            },
        };

        ResourceElement resourceElement = ToResourceElement(observation);

        // Simplified BP view - extract component values using forEach
        string viewDefinitionJson = """
            {
                "name": "blood_pressure_components",
                "resource": "Observation",
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "id" },
                            { "name": "status", "path": "status" }
                        ]
                    },
                    {
                        "forEach": "component",
                        "column": [
                            { "name": "component_code", "path": "code.coding.first().code" },
                            { "name": "component_value", "path": "value.ofType(Quantity).value" }
                        ]
                    }
                ]
            }
            """;

        // Act
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resourceElement);

        // Assert - should produce 2 rows (one per component)
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal("bp-1", r["id"]?.ToString()));

        var codes = result.Rows.Select(r => r["component_code"]?.ToString()).OrderBy(c => c).ToList();
        Assert.Contains("8480-6", codes);
        Assert.Contains("8462-4", codes);
    }

    private static ResourceElement ToResourceElement(Resource resource)
    {
        ITypedElement typedElement = resource.ToTypedElement();

        return new ResourceElement(typedElement);
    }
}
