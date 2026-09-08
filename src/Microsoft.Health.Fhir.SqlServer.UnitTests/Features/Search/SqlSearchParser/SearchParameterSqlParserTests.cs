// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
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

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterSqlParserTests
    {
        [Fact]
        public void GivenMultipleReverseChainEntriesInSameAndGroup_WhenParseMultiple_ThenUsesCombinedCteAndSharedCommandParameters()
        {
            // Arrange
            const short patientResourceTypeId = 1;
            const short observationResourceTypeId = 7;
            const short subjectSearchParamId = 11;
            const short identifierSearchParamId = 12;
            const short codeSearchParamId = 13;

            var queryParameters = new Dictionary<string, IList<string>>
            {
                ["_type"] = new List<string> { "Patient" },
                ["_has:Observation:subject:identifier"] = new List<string> { "http://hospital.example|MRN-123" },
                ["_has:Observation:subject:code"] = new List<string> { "http://loinc.org|4548-4" },
            };

            var fhirModel = CreateFhirModel(
                ("Patient", patientResourceTypeId),
                ("Observation", observationResourceTypeId));
            var resolvedPatientResourceTypeId = fhirModel.GetResourceTypeId("Patient");
            var definitionManager = CreateDefinitionManager(
                fhirModel,
                ("Observation", "subject", SearchParamType.Reference, subjectSearchParamId),
                ("Observation", "identifier", SearchParamType.Token, identifierSearchParamId),
                ("Observation", "code", SearchParamType.Token, codeSearchParamId));
            var parser = new SearchParameterSqlParser(
                definitionManager,
                fhirModel,
                Substitute.For<ICompartmentDefinitionManager>(),
                Substitute.For<ILogger<SearchParameterSqlParser>>());

            using var command = new SqlCommand();
            var parameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
            var options = CreateSqlSearchOptions(queryParameters);

            // Act
            var actualSql = parser.ParseMultiple(queryParameters, options, parameterManager, reuseQueryPlans: true);

            // Assert
            Assert.NotNull(actualSql);
            AssertContainsAll(
                actualSql,
                "cte0chain1 AS",
                "FROM cte0chain0_ref AS r",
                $"refTarget.ResourceTypeId IN ({resolvedPatientResourceTypeId})",
                "INNER JOIN dbo.TokenSearchParam AS t0 ON t0.ResourceSurrogateId = r.RefResourceSurrogateId AND t0.ResourceTypeId = r.RefResourceTypeId",
                "INNER JOIN dbo.TokenSearchParam AS t1 ON t1.ResourceSurrogateId = r.RefResourceSurrogateId AND t1.ResourceTypeId = r.RefResourceTypeId",
                "t0.SearchParamId = 12",
                "t1.SearchParamId = 13",
                "Value = @p0",
                "t0.Code = @p1",
                "Value = @p2",
                "t1.Code = @p3");
            AssertContainsNone(
                actualSql,
                "http://hospital.example",
                "MRN-123",
                "http://loinc.org",
                "4548-4");
            Assert.Collection(
                command.Parameters.Cast<SqlParameter>(),
                parameter => AssertParameter(parameter, "@p0", "http://hospital.example"),
                parameter => AssertParameter(parameter, "@p1", "MRN-123"),
                parameter => AssertParameter(parameter, "@p2", "http://loinc.org"),
                parameter => AssertParameter(parameter, "@p3", "4548-4"));
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

        private static SqlSearchOptions CreateSqlSearchOptions(IDictionary<string, IList<string>> queryParams)
        {
            var searchOptions = (SearchOptions)Activator.CreateInstance(typeof(SearchOptions), nonPublic: true)!;
            SetNonPublicProperty(searchOptions, nameof(SearchOptions.MaxItemCount), 10);
            SetNonPublicProperty(searchOptions, nameof(SearchOptions.IncludeCount), 10);
            SetNonPublicProperty(searchOptions, nameof(SearchOptions.UnsupportedSearchParams), Array.Empty<Tuple<string, string>>());
            SetNonPublicProperty(searchOptions, nameof(SearchOptions.Sort), Array.Empty<(SearchParameterInfo, Microsoft.Health.Fhir.Core.Features.Search.SortOrder)>());
            searchOptions.QueryParams = new Dictionary<string, IList<string>>(queryParams);

            return new SqlSearchOptions(searchOptions);
        }

        private static void SetNonPublicProperty<T>(object instance, string propertyName, T value)
        {
            instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(instance, new object[] { value! });
        }

        private static void AssertContainsAll(string sql, params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                Assert.Contains(fragment, sql, StringComparison.Ordinal);
            }
        }

        private static void AssertContainsNone(string sql, params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                Assert.DoesNotContain(fragment, sql, StringComparison.Ordinal);
            }
        }

        private static void AssertParameter(SqlParameter parameter, string expectedName, object expectedValue)
        {
            Assert.Equal(expectedName, parameter.ParameterName);
            Assert.Equal(expectedValue, parameter.Value);
        }
    }
}
