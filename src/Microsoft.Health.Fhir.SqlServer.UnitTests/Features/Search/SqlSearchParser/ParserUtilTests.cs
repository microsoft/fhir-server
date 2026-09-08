// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
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
    public class ParserUtilTests
    {
        [Fact]
        public void GivenDefaultOptions_WhenAddFirstCteFilters_ThenAddsHistoryAndDeletedChecks()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                ResourceVersionType = ResourceVersionType.Latest,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.Contains("r.IsHistory = 0", sql);
            Assert.Contains("r.IsDeleted = 0", sql);
        }

        [Fact]
        public void GivenHistoryOptions_WhenAddFirstCteFilters_ThenSkipsHistoryCheck()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                ResourceVersionType = ResourceVersionType.Latest | ResourceVersionType.History,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.DoesNotContain("r.IsHistory = 0", sql);
            Assert.Contains("r.IsDeleted = 0", sql);
        }

        [Fact]
        public void GivenSoftDeletedOptions_WhenAddFirstCteFilters_ThenSkipsDeletedCheck()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                ResourceVersionType = ResourceVersionType.Latest | ResourceVersionType.SoftDeleted,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.Contains("r.IsHistory = 0", sql);
            Assert.DoesNotContain("r.IsDeleted = 0", sql);
        }

        [Fact]
        public void GivenResourceTypes_WhenAddFirstCteFilters_ThenAddsResourceTypeInClause()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                ResourceTypes = new List<short> { 10, 20 },
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.Contains("r.ResourceTypeId IN (10, 20)", sql);
        }

        [Fact]
        public void GivenExcludedResourceTypes_WhenAddFirstCteFilters_ThenAddsNotInClause()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                ExcludedResourceTypes = new List<short> { 5 },
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.Contains("r.ResourceTypeId NOT IN (5)", sql);
        }

        [Fact]
        public void GivenLastCteName_WhenAddFirstCteFilters_ThenSkipsAllBaseFilters()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            var options = new ParserOptions
            {
                LastCteName = "cte0",
                ResourceTypes = new List<short> { 10 },
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();
            Assert.DoesNotContain("IsHistory", sql);
            Assert.DoesNotContain("IsDeleted", sql);
            Assert.DoesNotContain("ResourceTypeId IN", sql);
        }

        [Fact]
        public void GivenSingleCte_WhenAddUnionCte_ThenSelectsFromSingleCte()
        {
            var builder = new SqlQueryBuilder();

            // Need at least one CTE to exist so _isFirstCte is false
            builder.BeginCte("cte0");
            builder.Select("1");
            builder.EndCte();
            builder.AppendLine();

            ParserUtil.AddUnionCte(builder, "unionCte", new List<string> { "cte0" });
            var sql = builder.ToString();
            Assert.Contains("SELECT * FROM cte0", sql);
            Assert.DoesNotContain("UNION ALL", sql);
        }

        [Fact]
        public void GivenMultipleCtes_WhenAddUnionCte_ThenProducesUnionAll()
        {
            var builder = new SqlQueryBuilder();
            builder.BeginCte("cte0");
            builder.Select("1");
            builder.EndCte();
            builder.AppendLine();

            ParserUtil.AddUnionCte(builder, "unionCte", new List<string> { "cte0", "include0" });
            var sql = builder.ToString();
            Assert.Contains("SELECT * FROM cte0", sql);
            Assert.Contains("UNION ALL", sql);
            Assert.Contains("include0", sql);
            Assert.Contains("NOT EXISTS", sql);
        }

        [Fact]
        public void GivenIncludeSort_WhenAddUnionCte_ThenAddsSortValueNull()
        {
            var builder = new SqlQueryBuilder();
            builder.BeginCte("cte0");
            builder.Select("1");
            builder.EndCte();
            builder.AppendLine();

            ParserUtil.AddUnionCte(builder, "unionCte", new List<string> { "cte0", "inc0" }, includeSort: true);
            var sql = builder.ToString();
            Assert.Contains("SortValue = NULL", sql);
        }

        [Fact]
        public void GivenHistoryAndDeletedCheck_WhenBothIncluded_ThenNoChecksAdded()
        {
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");

            ParserUtil.AddHistoryAndDeletedCheck(builder, "r", includeHistory: true, includeDeleted: true);
            var sql = builder.ToString();
            Assert.DoesNotContain("IsHistory", sql);
            Assert.DoesNotContain("IsDeleted", sql);
        }

        [Fact]
        public void GivenContinuationToken_WhenAddFirstCteFilters_ThenUsesExcludedPagingParameters()
        {
            using var command = new SqlCommand();
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");
            var options = new ParserOptions
            {
                ContinuationToken = new ContinuationToken(new object[] { (short)10, 12345L }),
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();

            Assert.Contains("r.ResourceSurrogateId > @p0", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceTypeId = 10", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceTypeId > 10", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("12345", sql, StringComparison.Ordinal);
            Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Equal(12345L, command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.BigInt, command.Parameters["@p0"].SqlDbType);
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }

        [Fact]
        public void GivenLegacyContinuationTokenWithoutType_WhenAddFirstCteFilters_ThenUsesSurrogateOnlyPagingParameter()
        {
            using var command = new SqlCommand();
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");
            var options = new ParserOptions
            {
                ContinuationToken = new ContinuationToken(new object[] { 12345L }),
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();

            Assert.Contains("r.ResourceSurrogateId > @p0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ResourceTypeId = 0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ResourceTypeId > 0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("12345", sql, StringComparison.Ordinal);
            Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Equal(12345L, command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.BigInt, command.Parameters["@p0"].SqlDbType);
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }

        [Fact]
        public void GivenLastUpdatedSortContinuation_WhenAddFirstCteFilters_ThenUsesSurrogateOnlyPagingParameter()
        {
            using var command = new SqlCommand();
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");
            var options = new ParserOptions
            {
                ContinuationToken = new ContinuationToken(new object[] { (short)10, 12345L }),
                SortParameterName = KnownQueryParameterNames.LastUpdated,
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();

            Assert.Contains("r.ResourceSurrogateId > @p0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ResourceTypeId = 10", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ResourceTypeId > 10", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("12345", sql, StringComparison.Ordinal);
            Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Equal(12345L, command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.BigInt, command.Parameters["@p0"].SqlDbType);
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }

        [Fact]
        public void GivenIncludesContinuationToken_WhenAddFirstCteFilters_ThenUsesExcludedTypedSurrogateParameters()
        {
            using var command = new SqlCommand();
            var builder = new SqlQueryBuilder();
            builder.AppendLine("SELECT 1");
            builder.Where("1=1");
            var options = new ParserOptions
            {
                IncludesContinuationToken = new IncludesContinuationToken(new object[] { (short)15, 200L, 300L }),
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            ParserUtil.AddFirstCteFilters(builder, options, "r");
            var sql = builder.ToString();

            Assert.Contains("r.ResourceSurrogateId >= @p0", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceSurrogateId <= @p1", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceTypeId = 15", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("200", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("300", sql, StringComparison.Ordinal);
            Assert.Equal(
                new[] { 200L, 300L },
                command.Parameters.Cast<SqlParameter>().Select(parameter => (long)parameter.Value));
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }
    }
}
