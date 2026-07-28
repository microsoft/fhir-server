// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Filters patient-scoped candidates with ordinary FHIR search before semantic ranking.
    /// </summary>
    public sealed class SemanticSearchHandler : IRequestHandler<SemanticSearchRequest, SemanticSearchResponse>
    {
        private static readonly string[] SemanticResourceTypes =
        {
            ResourceType.DocumentReference.ToString(),
            ResourceType.Observation.ToString(),
            ResourceType.DiagnosticReport.ToString(),
        };

        private readonly ISearchService _searchService;
        private readonly IDocumentReferenceSemanticSearch _semanticSearch;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IDataResourceFilter _dataResourceFilter;
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
            ResourceDeserializer resourceDeserializer,
            IOptions<VectorSearchConfiguration> configuration)
        {
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _semanticSearch = EnsureArg.IsNotNull(semanticSearch, nameof(semanticSearch));
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _dataResourceFilter = EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));
            _resourceDeserializer = EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            _queryConfiguration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value.Query;
        }

        /// <inheritdoc />
        public async Task<SemanticSearchResponse> Handle(SemanticSearchRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            await _authorizationService.CheckAccess(DataActions.Read, true, cancellationToken);

            IReadOnlyCollection<string> selectedResourceTypes = request.ResourceTypes.Count == 0
                ? SemanticResourceTypes
                : request.ResourceTypes;
            var candidates = new List<ResourceWrapper>();
            foreach (string resourceType in selectedResourceTypes)
            {
                var searchParameters = new List<Tuple<string, string>>
                {
                    Tuple.Create("patient", request.PatientReference),
                    Tuple.Create("_count", _queryConfiguration.CandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                };
                SearchResult searchResult = await _searchService.SearchAsync(resourceType, searchParameters, cancellationToken);
                searchResult = _dataResourceFilter.Filter(searchResult);

                candidates.AddRange(searchResult.Results
                    .Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match)
                    .Select(result => result.Resource));
            }

            IReadOnlyList<VectorSearchResult> ranked = await _semanticSearch.SearchAsync(
                request.Query,
                candidates,
                request.Count,
                cancellationToken);
            Dictionary<(string ResourceTypeName, long ResourceSurrogateId), ResourceWrapper> candidatesById = candidates.ToDictionary(
                candidate => (candidate.ResourceTypeName, candidate.ResourceSurrogateId));

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Searchset,
                Total = ranked.Count,
                Entry = ranked
                    .Where(result => candidatesById.ContainsKey((result.ResourceTypeName, result.ResourceSurrogateId)))
                    .Select(result => new Bundle.EntryComponent
                    {
                        Resource = new RawResourceElement(candidatesById[(result.ResourceTypeName, result.ResourceSurrogateId)]).ToPoco(_resourceDeserializer),
                        Search = new Bundle.SearchComponent
                        {
                            Mode = Bundle.SearchEntryMode.Match,
                            Score = (decimal)result.Score,
                            Extension =
                            {
                                BundleFactory.CreateSemanticEvidenceExtension(result.Evidence),
                            },
                        },
                    })
                    .ToList(),
            };

            return new SemanticSearchResponse(bundle.ToResourceElement());
        }
    }
}
