// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class SqlServerSearchServiceReadResourceTests
{
    [Fact]
    public void TryExtractSimpleStringValue_WithStringExpression_ShouldReturnTrue()
    {
        // Arrange
        var stringExpression = Expression.StringEquals(FieldName.String, null, "test-value", false);

        // Act
        var result = InvokeTryExtractSimpleStringValue(stringExpression, out string value);

        // Assert
        Assert.True(result);
        Assert.Equal("test-value", value);
    }

    [Fact]
    public void TryExtractSimpleStringValue_WithNonStringExpression_ShouldReturnFalse()
    {
        // Arrange
        var notExpression = Expression.Not(Expression.StringEquals(FieldName.String, null, "test", false));

        // Act
        var result = InvokeTryExtractSimpleStringValue(notExpression, out string value);

        // Assert
        Assert.False(result);
        Assert.Null(value);
    }

    [Fact]
    public void TryExtractSimpleStringValue_WithEmptyString_ShouldReturnFalse()
    {
        // Arrange
        var stringExpression = Expression.StringEquals(FieldName.String, null, string.Empty, false);

        // Act
        var result = InvokeTryExtractSimpleStringValue(stringExpression, out string value);

        // Assert
        Assert.False(result);
        Assert.Equal(string.Empty, value); // The method still returns the empty string value
    }

    private static bool InvokeTryExtractSimpleStringValue(Expression expression, out string value)
    {
        // Use reflection to access the private static method for testing
        var method = typeof(SqlServerSearchService).GetMethod(
            "TryExtractSimpleStringValue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var parameters = new object[] { expression, null };
        var result = (bool)method.Invoke(null, parameters);
        value = (string)parameters[1];

        return result;
    }
}
