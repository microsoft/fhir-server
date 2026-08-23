// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Filters patient-scoped candidates with ordinary FHIR search before semantic ranking.
    /// </summary>
    public sealed class SemanticSearchHandler : IRequestHandler<SemanticSearchRequest, SemanticSearchResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IDocumentReferenceSemanticSearch _semanticSearch;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IDataResourceFilter _dataResourceFilter;
        private readonly ISemanticSearchEvidenceFilter _semanticSearchEvidenceFilter;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
        private readonly IVectorSearchParameterResolver _searchParameterResolver;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly VectorSearchQueryConfiguration _queryConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchHandler"/> class.
        /// </summary>
        public SemanticSearchHandler(
            ISearchService searchService,
            IDocumentReferenceSemanticSearch semanticSearch,
            IAuthorizationService<DataActions> authorizationService,
            IDataResourceFilter dataResourceFilter,
            ISemanticSearchEvidenceFilter semanticSearchEvidenceFilter,
            ICompartmentDefinitionManager compartmentDefinitionManager,
            IVectorSearchParameterResolver searchParameterResolver,
            ResourceDeserializer resourceDeserializer,
            IOptions<VectorSearchConfiguration> configuration)
        {
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _semanticSearch = EnsureArg.IsNotNull(semanticSearch, nameof(semanticSearch));
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _dataResourceFilter = EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));
            _semanticSearchEvidenceFilter = EnsureArg.IsNotNull(semanticSearchEvidenceFilter, nameof(semanticSearchEvidenceFilter));
            _compartmentDefinitionManager = EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            _searchParameterResolver = EnsureArg.IsNotNull(searchParameterResolver, nameof(searchParameterResolver));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            _queryConfiguration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value.Query;
        }

        /// <inheritdoc />
        public async Task<SemanticSearchResponse> Handle(SemanticSearchRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            await _authorizationService.CheckAccess(DataActions.Read, true, cancellationToken);

            if (!_compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> compartmentResourceTypes))
            {
                throw new InvalidOperationException("The Patient compartment definition is unavailable.");
            }

            var eligibleResourceTypes = compartmentResourceTypes
                .Where(resourceType => _searchParameterResolver.GetSearchParameters(resourceType).Count > 0)
                .ToHashSet(StringComparer.Ordinal);
            string unsupportedResourceType = request.ResourceTypes.FirstOrDefault(resourceType => !eligibleResourceTypes.Contains(resourceType));
            if (unsupportedResourceType != null)
            {
                throw new RequestNotValidException($"Resource type '{unsupportedResourceType}' is not eligible for patient semantic search.");
            }

            List<string> selectedResourceTypes = (request.ResourceTypes.Count == 0 ? eligibleResourceTypes : request.ResourceTypes)
                .OrderBy(resourceType => resourceType, StringComparer.Ordinal)
                .ToList();
            if (selectedResourceTypes.Count == 0)
            {
                return new SemanticSearchResponse(CreateBundle(Array.Empty<SearchResultEntry>(), Array.Empty<IReadOnlyList<SemanticSearchEvidence>>()).ToResourceElement());
            }

            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(SearchParameterNames.ResourceType, string.Join(',', selectedResourceTypes)),
                Tuple.Create(KnownQueryParameterNames.Count, _queryConfiguration.CandidateCount.ToString(CultureInfo.InvariantCulture)),
            };
            SearchResult searchResult = await _searchService.SearchCompartmentAsync(
                CompartmentType.Patient.ToString(),
                request.PatientId,
                resourceType: null,
                searchParameters,
                cancellationToken);
            searchResult = _dataResourceFilter.Filter(searchResult);
            List<ResourceWrapper> candidates = searchResult.Results
                .Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match)
                .Select(result => result.Resource)
                .ToList();

            IReadOnlyList<VectorSearchResult> ranked = await _semanticSearch.SearchAsync(
                request.Query,
                candidates,
                request.Count,
                cancellationToken);
            Dictionary<(string ResourceTypeName, long ResourceSurrogateId), ResourceWrapper> candidatesById = candidates.ToDictionary(
                candidate => (candidate.ResourceTypeName, candidate.ResourceSurrogateId));
            List<VectorSearchResult> returnedResults = ranked
                .Where(result => candidatesById.ContainsKey((result.ResourceTypeName, result.ResourceSurrogateId)))
                .ToList();
            var semanticSearchResult = new SearchResult(
                returnedResults.Select(result => new SearchResultEntry(
                    candidatesById[(result.ResourceTypeName, result.ResourceSurrogateId)],
                    ValueSets.SearchEntryMode.Match,
                    (decimal)result.Score,
                    evidenceItems: result.EvidenceItems)),
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
            semanticSearchResult = await _semanticSearchEvidenceFilter.FilterAsync(semanticSearchResult, cancellationToken);
            List<SearchResultEntry> returnedEntries = semanticSearchResult.Results.ToList();
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> rankedEvidence = SemanticSearchEvidenceRanker.AssignRanks(
                returnedEntries.Select(result => result.EvidenceItems).ToList());

            Bundle bundle = CreateBundle(returnedEntries, rankedEvidence);

            return new SemanticSearchResponse(bundle.ToResourceElement());
        }

        private Bundle CreateBundle(
            IReadOnlyList<SearchResultEntry> returnedEntries,
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> rankedEvidence)
        {
            return new Bundle
            {
                Type = Bundle.BundleType.Searchset,
                Total = returnedEntries.Count,
                Entry = returnedEntries
                    .Select((result, index) => new Bundle.EntryComponent
                    {
                        Resource = new RawResourceElement(result.Resource).ToPoco(_resourceDeserializer),
                        Search = new Bundle.SearchComponent
                        {
                            Mode = Bundle.SearchEntryMode.Match,
                            Score = result.Score,
                            Extension = rankedEvidence[index]
                                .Select(BundleFactory.CreateSemanticEvidenceExtension)
                                .ToList(),
                        },
                    })
                    .ToList(),
            };
        }
    }
}
