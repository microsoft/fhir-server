// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.SqlOnFhir.Operations;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="ViewDefinitionRunHandler"/> formatting logic.
/// </summary>
public class ViewDefinitionRunHandlerTests
{
    private static readonly List<Dictionary<string, object?>> SampleRows = new()
    {
        new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = "male", ["birth_date"] = "1990-01-15" },
        new Dictionary<string, object?> { ["id"] = "p2", ["gender"] = "female", ["birth_date"] = "1985-03-22" },
    };

    [Fact]
    public void GivenRows_WhenFormattedAsJson_ThenValidJsonArrayReturned()
    {
        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsJson(SampleRows);

        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(2, response.RowCount);
        Assert.StartsWith("[", response.FormattedOutput);
        Assert.EndsWith("]", response.FormattedOutput);
        Assert.Contains("\"p1\"", response.FormattedOutput);
        Assert.Contains("\"p2\"", response.FormattedOutput);
    }

    [Fact]
    public void GivenRows_WhenFormattedAsCsv_ThenHeaderAndDataRowsReturned()
    {
        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsCsv(SampleRows);

        Assert.Equal("text/csv", response.ContentType);
        Assert.Equal(2, response.RowCount);

        string[] lines = response.FormattedOutput.Split(Environment.NewLine);
        Assert.Equal("id,gender,birth_date", lines[0]);
        Assert.Contains("p1", lines[1]);
        Assert.Contains("p2", lines[2]);
    }

    [Fact]
    public void GivenRows_WhenFormattedAsNdjson_ThenOneJsonObjectPerLine()
    {
        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsNdjson(SampleRows);

        Assert.Equal("application/x-ndjson", response.ContentType);
        Assert.Equal(2, response.RowCount);

        string[] lines = response.FormattedOutput.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"p1\"", lines[0]);
        Assert.Contains("\"p2\"", lines[1]);
    }

    [Fact]
    public void GivenEmptyRows_WhenFormattedAsCsv_ThenEmptyStringReturned()
    {
        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsCsv(new List<Dictionary<string, object?>>());

        Assert.Equal("text/csv", response.ContentType);
        Assert.Equal(0, response.RowCount);
        Assert.Equal(string.Empty, response.FormattedOutput);
    }

    [Fact]
    public void GivenEmptyRows_WhenFormattedAsJson_ThenEmptyArrayReturned()
    {
        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsJson(new List<Dictionary<string, object?>>());

        Assert.Equal("[]", response.FormattedOutput);
        Assert.Equal(0, response.RowCount);
    }

    [Fact]
    public void GivenRowsWithCommas_WhenFormattedAsCsv_ThenValuesAreQuoted()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["name"] = "Smith, John", ["note"] = "regular" },
        };

        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsCsv(rows);

        Assert.Contains("\"Smith, John\"", response.FormattedOutput);
        Assert.Contains("regular", response.FormattedOutput);
    }

    [Fact]
    public void GivenRowsWithNulls_WhenFormattedAsJson_ThenNullsPreserved()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = null },
        };

        ViewDefinitionRunResponse response = ViewDefinitionRunHandler.FormatAsJson(rows);

        Assert.Contains("null", response.FormattedOutput);
    }

    [Fact]
    public void GivenFormatParam_WhenRouted_ThenCorrectFormatterUsed()
    {
        Assert.Equal("application/json", ViewDefinitionRunHandler.FormatResponse(SampleRows, "json").ContentType);
        Assert.Equal("text/csv", ViewDefinitionRunHandler.FormatResponse(SampleRows, "csv").ContentType);
        Assert.Equal("application/x-ndjson", ViewDefinitionRunHandler.FormatResponse(SampleRows, "ndjson").ContentType);
        Assert.Equal("application/json", ViewDefinitionRunHandler.FormatResponse(SampleRows, "unknown").ContentType);
    }
}
