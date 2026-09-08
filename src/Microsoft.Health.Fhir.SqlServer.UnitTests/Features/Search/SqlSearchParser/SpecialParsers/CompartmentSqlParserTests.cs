// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.SpecialParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CompartmentSqlParserTests
    {
        [Fact]
        public void GivenCompartmentSearch_WhenParse_ThenParameterizesOwnerIdAndKeepsStructuralIdsInline()
        {
            const short patientResourceTypeId = 1;
            const short observationResourceTypeId = 7;
            const short subjectSearchParamId = 11;

            var model = CreateFhirModel(
                ("Patient", patientResourceTypeId),
                ("Observation", observationResourceTypeId));
            var definitionManager = CreateDefinitionManager(
                model,
                ("Observation", "subject", SearchParamType.Reference, subjectSearchParamId));
            var compartmentDefinitionManager = CreateCompartmentDefinitionManager(
                CompartmentType.Patient,
                new HashSet<string>(StringComparer.Ordinal) { "Observation" },
                ("Observation", new HashSet<string>(StringComparer.Ordinal) { "subject" }));
            var parser = new CompartmentSqlParser(model, definitionManager, compartmentDefinitionManager);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            options.ResourceTypes.Add(patientResourceTypeId);
            options.ResourceTypes.Add(observationResourceTypeId);

            parser.Parse("Patient", "patient-id", options);
            var sql = options.SqlQueryBuilder.ToString();

            var ownerParameter = Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Equal("@p0", ownerParameter.ParameterName);
            Assert.Equal("patient-id", ownerParameter.Value);
            Assert.Contains("ref1.ReferenceResourceTypeId = 1", sql, StringComparison.Ordinal);
            Assert.Contains("ref1.ReferenceResourceId = @p0", sql, StringComparison.Ordinal);
            Assert.Contains("ref1.SearchParamId = 11", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceTypeId = 7", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("patient-id", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ref1.ReferenceResourceTypeId = @", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ref1.SearchParamId = @", sql, StringComparison.Ordinal);
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }

        [Fact]
        public void GivenSmartCompartmentSearch_WhenParse_ThenReusesSingleOwnerParameterAcrossMembershipAndOwnerBranches()
        {
            const short patientResourceTypeId = 1;
            const short observationResourceTypeId = 7;
            const short subjectSearchParamId = 11;

            var model = CreateFhirModel(
                ("Patient", patientResourceTypeId),
                ("Observation", observationResourceTypeId));
            var definitionManager = CreateDefinitionManager(
                model,
                ("Observation", "subject", SearchParamType.Reference, subjectSearchParamId));
            var compartmentDefinitionManager = CreateCompartmentDefinitionManager(
                CompartmentType.Patient,
                new HashSet<string>(StringComparer.Ordinal) { "Observation" },
                ("Observation", new HashSet<string>(StringComparer.Ordinal) { "subject" }));
            var parser = new SmartCompartmentSqlParser(model, definitionManager, compartmentDefinitionManager);
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            options.ResourceTypes.Add(patientResourceTypeId);
            options.ResourceTypes.Add(observationResourceTypeId);

            parser.Parse("Patient", "patient-id", options);
            var sql = options.SqlQueryBuilder.ToString();

            var ownerParameter = Assert.Single(command.Parameters.Cast<SqlParameter>(), p => Equals(p.Value, "patient-id"));
            Assert.Contains($"ref1.ReferenceResourceId = {ownerParameter.ParameterName}", sql, StringComparison.Ordinal);
            Assert.Contains($"AND r.ResourceId = {ownerParameter.ParameterName}", sql, StringComparison.Ordinal);
            Assert.Contains("ref1.ReferenceResourceTypeId = 1", sql, StringComparison.Ordinal);
            Assert.Contains("ref1.SearchParamId = 11", sql, StringComparison.Ordinal);
            Assert.Contains("r.ResourceTypeId = 7", sql, StringComparison.Ordinal);
            Assert.Contains("WHERE r.ResourceTypeId = 1", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("patient-id", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ref1.ReferenceResourceTypeId = @", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ref1.SearchParamId = @", sql, StringComparison.Ordinal);
            Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Empty(options.ParameterManager!.ParametersToHash);
        }

        private static ICompartmentDefinitionManager CreateCompartmentDefinitionManager(
            CompartmentType compartmentType,
            HashSet<string> resourceTypes,
            params (string resourceType, HashSet<string> searchParams)[] searchParameters)
        {
            var compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();

            compartmentDefinitionManager.TryGetResourceTypes(compartmentType, out Arg.Any<HashSet<string>>())
                .Returns(callInfo =>
                {
                    callInfo[1] = resourceTypes;
                    return true;
                });

            foreach (var (resourceType, parameterNames) in searchParameters)
            {
                compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out Arg.Any<HashSet<string>>())
                    .Returns(callInfo =>
                    {
                        callInfo[2] = parameterNames;
                        return true;
                    });
            }

            return compartmentDefinitionManager;
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
            params (string resourceTypeName, string searchParameterCode, SearchParamType searchParameterType, short searchParameterId)[] searchParameters)
        {
            var definitionManager = (SearchParameterDefinitionManager)RuntimeHelpers.GetUninitializedObject(typeof(SearchParameterDefinitionManager));
            definitionManager.UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>();

            var typeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>(StringComparer.OrdinalIgnoreCase);
            var searchParamIdsByUrl = new Dictionary<string, short>(StringComparer.Ordinal);

            foreach (var (resourceTypeName, searchParameterCode, searchParameterType, searchParameterId) in searchParameters)
            {
                var searchParameterUrl = new Uri($"http://example.org/SearchParameter/{resourceTypeName}-{searchParameterCode}");
                var searchParameterInfo = new SearchParameterInfo(searchParameterCode, searchParameterCode, searchParameterType, searchParameterUrl);
                definitionManager.UrlLookup[searchParameterUrl.OriginalString] = searchParameterInfo;
                searchParamIdsByUrl[searchParameterUrl.OriginalString] = searchParameterId;

                var codeLookup = typeLookup.GetOrAdd(
                    resourceTypeName,
                    static _ => new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase));
                codeLookup[searchParameterCode] = new ConcurrentQueue<string>(new[] { searchParameterUrl.OriginalString });
            }

            definitionManager.TypeLookup = typeLookup;
            typeof(SearchParameterDefinitionManager).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(definitionManager, true);

            fhirModel.GetSearchParamId(Arg.Any<Uri>()).Returns(callInfo =>
            {
                var searchParameterUri = Assert.IsType<Uri>(callInfo[0]);
                return searchParamIdsByUrl[searchParameterUri.OriginalString];
            });

            return new SqlSearchParameterDefinitionManager(definitionManager, fhirModel);
        }
    }
}
