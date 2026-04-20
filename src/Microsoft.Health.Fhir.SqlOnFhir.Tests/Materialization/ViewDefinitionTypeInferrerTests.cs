// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionTypeInferrer"/>.
/// </summary>
public class ViewDefinitionTypeInferrerTests
{
    [Theory]
    [InlineData("effective.ofType(dateTime)", null, null, "dateTime")]
    [InlineData("value.ofType(Quantity)", null, null, "Quantity")]
    [InlineData("Observation.value.ofType(boolean)", null, null, "boolean")]
    public void GivenOfTypeAtEndOfPath_WhenResolvingType_ThenOfTypeWins(
        string path, string? explicitType, string? evaluatorType, string expected)
    {
        string? result = ViewDefinitionTypeInferrer.ResolveType(path, explicitType, evaluatorType);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("value.ofType(Quantity).value", "decimal")]
    [InlineData("value.ofType(Quantity).code", "code")]
    [InlineData("value.ofType(Quantity).unit", "string")]
    [InlineData("value.ofType(Quantity).system", "uri")]
    [InlineData("component.value.ofType(Quantity).value", "decimal")]
    public void GivenOfTypeComplexMember_WhenResolvingType_ThenMemberTypeReturned(string path, string expected)
    {
        string? result = ViewDefinitionTypeInferrer.ResolveType(path, explicitType: null, evaluatorType: null);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("getResourceKey()")]
    [InlineData("subject.getReferenceKey(Patient)")]
    [InlineData("id.getResourceKey()")]
    public void GivenHelperFunction_WhenResolvingType_ThenIdReturned(string path)
    {
        string? result = ViewDefinitionTypeInferrer.ResolveType(path, explicitType: null, evaluatorType: null);
        Assert.Equal("id", result);
    }

    [Fact]
    public void GivenExplicitType_WhenResolvingType_ThenExplicitWinsOverPath()
    {
        string? result = ViewDefinitionTypeInferrer.ResolveType(
            path: "value.ofType(Quantity).value",
            explicitType: "integer",
            evaluatorType: "decimal");

        Assert.Equal("integer", result);
    }

    [Fact]
    public void GivenNoHintsAndEvaluatorType_WhenResolvingType_ThenEvaluatorTypeReturned()
    {
        string? result = ViewDefinitionTypeInferrer.ResolveType(
            path: "gender",
            explicitType: null,
            evaluatorType: "code");

        Assert.Equal("code", result);
    }

    [Fact]
    public void GivenViewDefinitionJson_WhenExtractingColumnMetadata_ThenAllColumnsFound()
    {
        const string vdJson = """
            {
                "resourceType": "ViewDefinition",
                "name": "observation_bp",
                "resource": "Observation",
                "select": [
                    {
                        "column": [
                            { "name": "id", "path": "getResourceKey()" },
                            { "name": "patient_id", "path": "subject.getReferenceKey(Patient)" },
                            { "name": "effective_date_time", "path": "effective.ofType(dateTime)" }
                        ]
                    },
                    {
                        "column": [
                            { "name": "sbp_value", "path": "value.ofType(Quantity).value" },
                            { "name": "sbp_unit", "path": "value.ofType(Quantity).unit" },
                            { "name": "explicit_col", "path": "valueBoolean", "type": "boolean" }
                        ],
                        "forEach": "component.first()"
                    }
                ]
            }
            """;

        var meta = ViewDefinitionTypeInferrer.ExtractColumnMetadata(vdJson);

        Assert.Equal(6, meta.Count);
        Assert.Equal("getResourceKey()", meta["id"].Path);
        Assert.Null(meta["id"].ExplicitType);
        Assert.Equal("boolean", meta["explicit_col"].ExplicitType);

        // End-to-end: resolution through inferrer.
        Assert.Equal("id", ViewDefinitionTypeInferrer.ResolveType(meta["id"].Path, meta["id"].ExplicitType, null));
        Assert.Equal("id", ViewDefinitionTypeInferrer.ResolveType(meta["patient_id"].Path, meta["patient_id"].ExplicitType, null));
        Assert.Equal("dateTime", ViewDefinitionTypeInferrer.ResolveType(meta["effective_date_time"].Path, meta["effective_date_time"].ExplicitType, null));
        Assert.Equal("decimal", ViewDefinitionTypeInferrer.ResolveType(meta["sbp_value"].Path, meta["sbp_value"].ExplicitType, null));
        Assert.Equal("string", ViewDefinitionTypeInferrer.ResolveType(meta["sbp_unit"].Path, meta["sbp_unit"].ExplicitType, null));
        Assert.Equal("boolean", ViewDefinitionTypeInferrer.ResolveType(meta["explicit_col"].Path, meta["explicit_col"].ExplicitType, null));
    }

    [Fact]
    public void GivenMalformedJson_WhenExtractingColumnMetadata_ThenEmptyMapReturned()
    {
        var meta = ViewDefinitionTypeInferrer.ExtractColumnMetadata("{not valid json");
        Assert.Empty(meta);
    }
}
