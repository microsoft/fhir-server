// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SpecialParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludeSqlParserTests
    {
        [Fact]
        public void GivenInclude_WhenParse_ThenUsesParameterizedTopThresholdAndRowLimitPolicies()
        {
            const short observationResourceTypeId = 7;
            const short patientResourceTypeId = 1;
            const short subjectSearchParamId = 11;

            var model = CreateFhirModel(("Observation", observationResourceTypeId), ("Patient", patientResourceTypeId));
            var parser = new IncludeSqlParser(
                CreateDefinitionManager(model, ("Observation", "subject", SearchParamType.Reference, subjectSearchParamId)),
                model);
            using var command = new SqlCommand();
            var options = new ParserOptions
            {
                CteNumber = 2,
                LastCteName = "cte1",
                Count = 4,
                IncludeCount = 6,
                SqlQueryBuilder = new SqlQueryBuilder(),
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            parser.Parse("_include", "Observation:subject:Patient", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("DISTINCT TOP (@p0)", sql, StringComparison.Ordinal);
            Assert.Contains("count_big(*) over() > @p1", sql, StringComparison.Ordinal);
            Assert.Contains("lcte.Row <= @p2", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("TOP 7", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("'@p0'", sql, StringComparison.Ordinal);
            Assert.Collection(
                command.Parameters.Cast<SqlParameter>(),
                parameter =>
                {
                    Assert.Equal("@p0", parameter.ParameterName);
                    Assert.Equal(7, parameter.Value);
                    Assert.Equal(SqlDbType.Int, parameter.SqlDbType);
                },
                parameter =>
                {
                    Assert.Equal("@p1", parameter.ParameterName);
                    Assert.Equal(6, parameter.Value);
                    Assert.Equal(SqlDbType.Int, parameter.SqlDbType);
                },
                parameter =>
                {
                    Assert.Equal("@p2", parameter.ParameterName);
                    Assert.Equal(4, parameter.Value);
                    Assert.Equal(SqlDbType.Int, parameter.SqlDbType);
                });
            Assert.Equal(
                new[] { "@p1", "@p2" },
                options.ParameterManager!.ParametersToHash.Select(parameter => parameter.ParameterName).OrderBy(name => name));
        }

        [Fact]
        public void GivenIncludesContinuationToken_WhenParse_ThenUsesExcludedTypedContinuationParameters()
        {
            const short observationResourceTypeId = 7;
            const short patientResourceTypeId = 1;
            const short subjectSearchParamId = 11;

            var model = CreateFhirModel(("Observation", observationResourceTypeId), ("Patient", patientResourceTypeId));
            var parser = new IncludeSqlParser(
                CreateDefinitionManager(model, ("Observation", "subject", SearchParamType.Reference, subjectSearchParamId)),
                model);
            using var command = new SqlCommand();
            var options = new ParserOptions
            {
                CteNumber = 2,
                LastCteName = "cte1",
                Count = 4,
                IncludeCount = 6,
                SqlQueryBuilder = new SqlQueryBuilder(),
                IncludesContinuationToken = new IncludesContinuationToken(new object[] { patientResourceTypeId, 100L, 200L, patientResourceTypeId, 300L }),
                ParameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                ReuseQueryPlans = true,
            };

            parser.Parse("_include", "Observation:subject:Patient", options);
            var sql = options.SqlQueryBuilder.ToString();

            Assert.Contains("refTarget.ResourceSurrogateId > @p3", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("300", sql, StringComparison.Ordinal);
            Assert.Equal(300L, command.Parameters["@p3"].Value);
            Assert.Equal(SqlDbType.BigInt, command.Parameters["@p3"].SqlDbType);
            Assert.Equal(
                new[] { "@p1", "@p2" },
                options.ParameterManager!.ParametersToHash.Select(parameter => parameter.ParameterName).OrderBy(name => name));
        }

        private static SqlSearchParameterDefinitionManager CreateDefinitionManager(
            ISqlServerFhirModel fhirModel,
            params (string resourceTypeName, string searchParameterCode, SearchParamType searchParamType, short searchParameterId)[] searchParameters)
        {
            var definitionManager = (SearchParameterDefinitionManager)RuntimeHelpers.GetUninitializedObject(typeof(SearchParameterDefinitionManager));
            definitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>();

            var typeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>(StringComparer.OrdinalIgnoreCase);
            var searchParamIdsByUrl = new Dictionary<string, short>(StringComparer.Ordinal);

            foreach (var (resourceTypeName, searchParameterCode, searchParamType, searchParameterId) in searchParameters)
            {
                var searchParameterUrl = new Uri($"http://example.org/SearchParameter/{resourceTypeName}-{searchParameterCode}");
                var searchParameterInfo = new SearchParameterInfo(searchParameterCode, searchParameterCode, searchParamType, searchParameterUrl);
                definitionManager.UrlLookup[searchParameterUrl.OriginalString] = searchParameterInfo;
                searchParamIdsByUrl[searchParameterUrl.OriginalString] = searchParameterId;

                var codeLookup = typeLookup.GetOrAdd(
                    resourceTypeName,
                    _ => new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase));
                codeLookup[searchParameterCode] = new ConcurrentQueue<string>(new[] { searchParameterUrl.OriginalString });
            }

            definitionManager.TypeLookup = typeLookup;
            typeof(SearchParameterDefinitionManager).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(definitionManager, true);

            fhirModel.GetSearchParamId(Arg.Any<Uri>()).Returns(callInfo => searchParamIdsByUrl[((Uri)callInfo[0]).OriginalString]);

            return new SqlSearchParameterDefinitionManager(definitionManager, fhirModel);
        }

        private static ISqlServerFhirModel CreateFhirModel(params (string resourceType, short id)[] resourceTypes)
        {
            var model = ParserTestHelper.CreateMockFhirModel(resourceTypes);

            foreach (var (resourceType, id) in resourceTypes)
            {
                model.GetResourceTypeId(resourceType).Returns(id);
                model.GetResourceTypeName(id).Returns(resourceType);
            }

            return model;
        }
    }
}
