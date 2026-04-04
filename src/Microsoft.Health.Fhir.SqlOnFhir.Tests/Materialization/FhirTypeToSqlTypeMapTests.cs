// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="FhirTypeToSqlTypeMap"/>.
/// </summary>
public class FhirTypeToSqlTypeMapTests
{
    [Theory]
    [InlineData("string", "nvarchar(max)")]
    [InlineData("uri", "nvarchar(4000)")]
    [InlineData("url", "nvarchar(4000)")]
    [InlineData("canonical", "nvarchar(4000)")]
    [InlineData("code", "nvarchar(256)")]
    [InlineData("id", "nvarchar(128)")]
    [InlineData("oid", "nvarchar(256)")]
    [InlineData("uuid", "nvarchar(64)")]
    [InlineData("markdown", "nvarchar(max)")]
    [InlineData("base64Binary", "nvarchar(max)")]
    public void GivenAStringLikeFhirType_WhenMapped_ThenCorrectSqlTypeReturned(string fhirType, string expectedSqlType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(expectedSqlType, result);
    }

    [Theory]
    [InlineData("boolean", "bit")]
    [InlineData("integer", "int")]
    [InlineData("integer64", "bigint")]
    [InlineData("positiveInt", "int")]
    [InlineData("unsignedInt", "int")]
    [InlineData("decimal", "decimal(18, 9)")]
    public void GivenANumericFhirType_WhenMapped_ThenCorrectSqlTypeReturned(string fhirType, string expectedSqlType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(expectedSqlType, result);
    }

    [Theory]
    [InlineData("date", "date")]
    [InlineData("dateTime", "datetime2(7)")]
    [InlineData("instant", "datetime2(7)")]
    [InlineData("time", "time(7)")]
    public void GivenADateTimeFhirType_WhenMapped_ThenCorrectSqlTypeReturned(string fhirType, string expectedSqlType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(expectedSqlType, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenNullOrEmptyFhirType_WhenMapped_ThenDefaultSqlTypeReturned(string? fhirType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(FhirTypeToSqlTypeMap.DefaultSqlType, result);
    }

    [Theory]
    [InlineData("UnknownType")]
    [InlineData("ComplexType")]
    [InlineData("MyCustomType")]
    public void GivenAnUnknownFhirType_WhenMapped_ThenDefaultSqlTypeReturned(string fhirType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(FhirTypeToSqlTypeMap.DefaultSqlType, result);
    }

    [Theory]
    [InlineData("BOOLEAN", "bit")]
    [InlineData("Boolean", "bit")]
    [InlineData("DATETIME", "datetime2(7)")]
    [InlineData("DateTime", "datetime2(7)")]
    [InlineData("INTEGER", "int")]
    [InlineData("Integer", "int")]
    public void GivenCaseVariations_WhenMapped_ThenMatchesAreFound(string fhirType, string expectedSqlType)
    {
        string result = FhirTypeToSqlTypeMap.GetSqlType(fhirType);
        Assert.Equal(expectedSqlType, result);
    }

    [Fact]
    public void AllMappings_ShouldContainExpectedEntries()
    {
        var mappings = FhirTypeToSqlTypeMap.AllMappings;

        Assert.NotEmpty(mappings);
        Assert.True(mappings.Count >= 20, "Expected at least 20 type mappings");
        Assert.True(mappings.ContainsKey("string"));
        Assert.True(mappings.ContainsKey("boolean"));
        Assert.True(mappings.ContainsKey("dateTime"));
        Assert.True(mappings.ContainsKey("decimal"));
    }
}
