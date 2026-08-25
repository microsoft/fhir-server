// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Search.Queries
{
    /// <summary>
    /// Covers the id-only projection, which conditional deletes run their match search with. A resource the
    /// search matched is later written against under an optimistic concurrency guard built from the version the
    /// search observed, so anything the projection drops silently disables that guard.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QueryBuilderProjectionTests
    {
        private static readonly CosmosSerializer DocumentSerializer =
            new FhirCosmosClientInitializer.FhirCosmosSerializer(NullLogger<FhirCosmosClientInitializer>.Instance);

        [Fact]
        public void GivenAnIdOnlySearch_WhenTheQueryIsBuilt_ThenTheProjectedDocumentCarriesTheObservedVersion()
        {
            QueryDefinition query = BuildIdOnlyQuery(includes: null);

            FhirCosmosResourceWrapper searchResult = DeserializeProjectionOf(query, CreateStoredDocument(version: "3"));

            Assert.Equal("Observation", searchResult.ResourceTypeName);
            Assert.Equal("conditional-delete-target", searchResult.ResourceId);
            Assert.Equal("3", searchResult.Version);
        }

        [Fact]
        public void GivenAnIdOnlySearchWithIncludes_WhenTheQueryIsBuilt_ThenTheProjectedDocumentCarriesTheObservedVersion()
        {
            // A conditional delete with _include runs exactly this query for its match, and only its single
            // match is version guarded.
            QueryDefinition query = BuildIdOnlyQuery(includes: new[]
            {
                Expression.Include(
                    new[] { "Observation" },
                    new SearchParameterInfo("subject", "subject"),
                    sourceResourceType: null,
                    targetResourceType: "Patient",
                    referencedTypes: new[] { "Patient" },
                    wildCard: false,
                    reversed: false,
                    iterate: false),
            });

            FhirCosmosResourceWrapper searchResult = DeserializeProjectionOf(query, CreateStoredDocument(version: "3"));

            Assert.Equal("3", searchResult.Version);
        }

        [Fact]
        public void GivenAnIdOnlySearchOfAResourceVersionedByItsETag_WhenTheQueryIsBuilt_ThenTheProjectedDocumentCarriesTheObservedVersion()
        {
            // A resource that has never been updated has no "version" property at all - its version is its
            // _etag - so projecting one field without the other still loses the version for those resources.
            JObject storedDocument = CreateStoredDocument(version: "3");
            storedDocument.Remove(KnownResourceWrapperProperties.Version);

            QueryDefinition query = BuildIdOnlyQuery(includes: null);

            FhirCosmosResourceWrapper searchResult = DeserializeProjectionOf(query, storedDocument);

            Assert.Equal("0000d986-0000-0700-0000-5f9d1b7e0000", searchResult.Version);
        }

        private static QueryDefinition BuildIdOnlyQuery(IReadOnlyList<IncludeExpression> includes)
        {
            var searchOptions = new SearchOptions
            {
                OnlyIds = true,
                ResourceVersionTypes = ResourceVersionType.Latest,
                Sort = Array.Empty<(SearchParameterInfo SearchParameterInfo, SortOrder SortOrder)>(),
            };

            return new QueryBuilder().BuildSqlQuerySpec(searchOptions, new QueryBuilderOptions(includes, QueryProjection.IdAndType));
        }

        private static JObject CreateStoredDocument(string version)
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "conditional-delete-target";
            observation.VersionId = version;
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(
                typedElement,
                new RawResourceFactory(new FhirJsonSerializer()).Create(typedElement, keepMeta: true, keepVersion: true),
                new ResourceRequest(HttpMethod.Put, "http://fhir"),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            wrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("code", "code"), new StringSearchValue("body-weight")),
            };

            using Stream stream = DocumentSerializer.ToStream(new FhirCosmosResourceWrapper(wrapper));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            JObject document = JObject.Parse(reader.ReadToEnd());
            document[KnownDocumentProperties.ETag] = "\"0000d986-0000-0700-0000-5f9d1b7e0000\"";

            return document;
        }

        /// <summary>
        /// Narrows a stored document down to the fields <paramref name="query"/> projects and deserializes the
        /// result the way the search service does, so the test sees exactly what a search would hand back.
        /// </summary>
        /// <param name="query">The query whose projection should be applied.</param>
        /// <param name="storedDocument">The document as it is stored in the container.</param>
        private static FhirCosmosResourceWrapper DeserializeProjectionOf(QueryDefinition query, JObject storedDocument)
        {
            string selectList = query.QueryText.Substring(0, query.QueryText.IndexOf("FROM root r", StringComparison.Ordinal));

            // Sub-selects (the array of referenced ids an _include search projects) are built separately and
            // are not part of the fields taken from the matched document itself.
            int subSelect = selectList.IndexOf("ARRAY(", StringComparison.Ordinal);
            if (subSelect >= 0)
            {
                selectList = selectList.Substring(0, subSelect);
            }

            var projectedDocument = new JObject();
            foreach (Match match in Regex.Matches(selectList, @"\br\.(?<field>[A-Za-z_][A-Za-z0-9_]*)"))
            {
                string field = match.Groups["field"].Value;

                if (storedDocument.TryGetValue(field, out JToken value))
                {
                    projectedDocument[field] = value;
                }
            }

            Assert.NotEmpty(projectedDocument);

            return DocumentSerializer.FromStream<FhirCosmosResourceWrapper>(
                new MemoryStream(Encoding.UTF8.GetBytes(projectedDocument.ToString(Formatting.None))));
        }
    }
}
