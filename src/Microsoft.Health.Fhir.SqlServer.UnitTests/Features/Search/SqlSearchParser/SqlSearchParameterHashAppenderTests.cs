// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchParameterHashAppenderTests
    {
        [Fact]
        public void GivenHashableValuesAndReuseDisabled_WhenAppend_ThenAddsLegacyHashComment()
        {
            // Arrange
            using var command = new SqlCommand();
            var manager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
            manager.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: true);
            var builder = new SqlQueryBuilder().AppendLine("SELECT 1");

            // Act
            SqlSearchParameterHashAppender.Append(builder, manager, reuseQueryPlans: false);

            // Assert
            var sql = builder.ToString();
            Assert.Contains(SqlSearchConstants.ParametersHashStart, sql, StringComparison.Ordinal);
            Assert.Contains("params=@p0", sql, StringComparison.Ordinal);
            Assert.Contains(SqlSearchConstants.ParametersHashEnd, sql, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenHashableValuesAndReuseEnabled_WhenAppend_ThenAddsNoComment()
        {
            // Arrange
            using var command = new SqlCommand();
            var manager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
            manager.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: true);
            var builder = new SqlQueryBuilder().AppendLine("SELECT 1");
            var expectedSql = builder.ToString();

            // Act
            SqlSearchParameterHashAppender.Append(builder, manager, reuseQueryPlans: true);

            // Assert
            Assert.Equal(expectedSql, builder.ToString());
        }

        [Fact]
        public void GivenNoHashableValuesAndReuseDisabled_WhenAppend_ThenAddsNoComment()
        {
            // Arrange
            using var command = new SqlCommand();
            var manager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
            manager.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: false);
            var builder = new SqlQueryBuilder().AppendLine("SELECT 1");
            var expectedSql = builder.ToString();

            // Act
            SqlSearchParameterHashAppender.Append(builder, manager, reuseQueryPlans: false);

            // Assert
            Assert.Equal(expectedSql, builder.ToString());
        }
    }
}
