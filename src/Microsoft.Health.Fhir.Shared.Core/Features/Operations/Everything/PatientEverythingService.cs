// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public class PatientEverythingService : IPatientEverythingService
    {
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly IUrlResolver _urlResolver;

        private IReadOnlyList<string> _includes = new[] { "general-practitioner", "organization" };
        private readonly (string resourceType, string searchParameterName) _revinclude = new("Device", "patient");

        // Limit the total number of "seealso" links because we store them in the continuation token, and the token
        // has limited storage space.
        // This number was selected considering the everything token could contain an internal continuation token which
        // could be up to 2kB and resource ids can be up to 64B.
        // Note: we plan to remove this cap in the future (see AB#85289).
        private readonly int _seeAlsoLinkCountThreshold = 10;

        public PatientEverythingService(
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchOptionsFactory searchOptionsFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ICompartmentDefinitionManager compartmentDefinitionManager,
            IReferenceSearchValueParser referenceSearchValueParser,
            IResourceDeserializer resourceDeserializer,
            IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _searchServiceFactory = searchServiceFactory;
            _searchOptionsFactory = searchOptionsFactory;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _compartmentDefinitionManager = compartmentDefinitionManager;
            _referenceSearchValueParser = referenceSearchValueParser;
            _resourceDeserializer = resourceDeserializer;
            _urlResolver = urlResolver;
        }

        public async Task<SearchResult> SearchAsync(
            string resourceId,
            PartialDateTime start,
            PartialDateTime end,
            PartialDateTime since,
            string type,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            EverythingOperationContinuationToken token = string.IsNullOrEmpty(continuationToken)
                ? new EverythingOperationContinuationToken()
                : EverythingOperationContinuationToken.FromString(ContinuationTokenConverter.Decode(continuationToken));

            if (token == null || token.Phase < 0 || token.Phase > 3)
            {
                throw new BadRequestException(Core.Resources.InvalidContinuationToken);
            }

            SearchResult searchResult;
            string encodedInternalContinuationToken = string.IsNullOrEmpty(token.InternalContinuationToken)
                ? null
                : ContinuationTokenConverter.Encode(token.InternalContinuationToken);
            IReadOnlyList<string> types = string.IsNullOrEmpty(type) ? new List<string>() : type.SplitByOrSeparator();

            // We will need to store the id of the patient that links to this one if we are processing a "seealso" link
            var parentPatientId = resourceId;

            // Check if we are currently processing a Patient's "seealso" link
            resourceId = token.IsProcessingSeeAlsoLink ? token.CurrentSeeAlsoLinkId : resourceId;

            var phase = token.Phase;
            switch (phase)
            {
                case 0:
                    searchResult = await SearchIncludes(resourceId, since, types, token, cancellationToken);

                    if (!searchResult.Results.Any())
                    {
                        phase = 1;
                        goto case 1;
                    }

                    break;
                case 1:
                    // If both start and end are null, we can just perform regular compartment search in Phase 2
                    if (start == null && end == null)
                    {
                        phase = 2;
                        goto case 2;
                    }

                    searchResult = await SearchCompartmentWithDate(resourceId, start, end, since, types, encodedInternalContinuationToken, cancellationToken);
                    if (!searchResult.Results.Any())
                    {
                        phase = 2;
                        goto case 2;
                    }

                    break;
                case 2:
                    searchResult = start == null && end == null
                        ? await SearchCompartment(resourceId, parentPatientId, since, types, encodedInternalContinuationToken, token, cancellationToken)
                        : await SearchCompartmentWithoutDate(resourceId, parentPatientId, since, types, encodedInternalContinuationToken, token, cancellationToken);

                    if (!searchResult.Results.Any())
                    {
                        phase = 3;
                        goto case 3;
                    }

                    break;
                case 3:
                    searchResult = await SearchRevinclude(resourceId, since, types, encodedInternalContinuationToken, cancellationToken);
                    break;
                default:
                    // This should never happen
                    throw new EverythingOperationException(string.Format(Core.Resources.InvalidEverythingOperationPhase, phase), HttpStatusCode.BadRequest);
            }

            string nextContinuationToken = null;
            if (searchResult.ContinuationToken != null)
            {
                // Keep processing the remaining results for the current phase
                token.InternalContinuationToken = searchResult.ContinuationToken;

                nextContinuationToken = token.ToString();
            }
            else if (phase < 3)
            {
                // Advance to the next phase
                token.Phase = phase + 1;
                token.InternalContinuationToken = null;

                nextContinuationToken = token.ToString();
            }
            else if (token.MoreSeeAlsoLinksToProcess)
            {
                token.Phase = 0;
                token.InternalContinuationToken = null;
                token.ProcessNextSeeAlsoLink();

                nextContinuationToken = token.ToString();
            }

            return new SearchResult(searchResult.Results, nextContinuationToken, searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
        }

        private async Task<SearchResult> SearchIncludes(
            string resourceId,
            PartialDateTime since,
            IReadOnlyList<string> types,
            EverythingOperationContinuationToken token,
            CancellationToken cancellationToken)
        {
            using IScoped<ISearchService> search = _searchServiceFactory();
            var searchResultEntries = new List<SearchResultEntry>();

            // Build search parameters
            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(SearchParameterNames.Id, resourceId),
            };

            searchParameters.AddRange(_includes.Select(include => Tuple.Create(SearchParameterNames.Include, $"{ResourceType.Patient}:{include}")));

            // Search
            SearchOptions searchOptions = _searchOptionsFactory.Create(ResourceType.Patient.ToString(), searchParameters);
            SearchResult searchResult = await search.Value.SearchAsync(searchOptions, cancellationToken);
            searchResultEntries.AddRange(searchResult.Results.Select(x => new SearchResultEntry(x.Resource)));

            if (!token.IsProcessingSeeAlsoLink)
            {
                StoreSeeAlsoLinkInformationInToken(resourceId, token, searchResultEntries);
            }

            // Filter results by _type
            if (types.Any())
            {
                searchResultEntries = searchResultEntries.Where(s => types.Contains(s.Resource.ResourceTypeName)).ToList();
            }

            // Filter results by _since
            if (since != null)
            {
                var sinceDateTimeOffset = since.ToDateTimeOffset(
                    defaultMonth: 1,
                    defaultDaySelector: (year, month) => 1,
                    defaultHour: 0,
                    defaultMinute: 0,
                    defaultSecond: 0,
                    defaultFraction: 0.0000000m,
                    defaultUtcOffset: TimeSpan.Zero);
                searchResultEntries = searchResultEntries.Where(s => s.Resource.LastModified.CompareTo(sinceDateTimeOffset) >= 0).ToList();
            }

            return new SearchResult(searchResultEntries, searchResult.ContinuationToken, searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
        }

        private void StoreSeeAlsoLinkInformationInToken(string resourceId, EverythingOperationContinuationToken token, IReadOnlyCollection<SearchResultEntry> searchResultEntries)
        {
            SearchResultEntry patientResource = searchResultEntries.FirstOrDefault(s => string.Equals(s.Resource.ResourceTypeName, ResourceType.Patient.ToString(), StringComparison.Ordinal));

            if (patientResource.Resource != null)
            {
                ResourceElement element = _resourceDeserializer.Deserialize(patientResource.Resource);
                Patient patient = element.ToPoco<Patient>();

                List<Patient.LinkComponent> links = patient.Link;

                if (links == null)
                {
                    // No links to store
                    return;
                }

                // Track the number of "seealso" links, since there is a limit to how many we can store in the continuation token
                int uniqueSeeAlsoLinkCount = 0;

                foreach (Patient.LinkComponent link in links)
                {
                    ReferenceSearchValue referenceSearchValue = _referenceSearchValueParser.Parse(link.Other.Reference);

                    // If the link points back to the current patient
                    if (string.Equals(referenceSearchValue.ResourceId, resourceId, StringComparison.Ordinal))
                    {
                        // Ignore it to avoid running patient $everything on the same patient again
                        continue;
                    }

                    if (link.Type == Patient.LinkType.ReplacedBy)
                    {
                        var url = _urlResolver.ResolveOperationResultUrl(OperationsConstants.PatientEverything, referenceSearchValue.ResourceId);

                        throw new EverythingOperationException(
                            string.Format(
                                Core.Resources.EverythingOperationResourceIrrelevant,
                                resourceId,
                                referenceSearchValue.ResourceId),
                            HttpStatusCode.MovedPermanently,
                            url.ToString());
                    }

                    if (link.Type == Patient.LinkType.Seealso)
                    {
                        // Links can be of type Patient or RelatedPerson - only attempt to run the $everything operation on Patient resources
                        if (string.Equals(referenceSearchValue.ResourceType, ResourceType.Patient.ToString(), StringComparison.Ordinal))
                        {
                            if (!token.SeeAlsoLinks.Contains(referenceSearchValue.ResourceId))
                            {
                                token.SeeAlsoLinks.Add(referenceSearchValue.ResourceId);
                                uniqueSeeAlsoLinkCount++;

                                if (uniqueSeeAlsoLinkCount > _seeAlsoLinkCountThreshold)
                                {
                                    // Instead of returning an error, we plan to retrieve the patient resource and extract the remaining links to process (see AB#85289).
                                    throw new EverythingOperationException(string.Format(Core.Resources.EverythingOperationMaxSeeAlsoLinksReached, resourceId, 10), HttpStatusCode.BadRequest);
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task<SearchResult> SearchRevinclude(
            string resourceId,
            PartialDateTime since,
            IReadOnlyList<string> types,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            // R5 include device into compartment so no sence to do search.
            // But if we expand _revinclude to be a list this should be revisted!
            if ((ModelInfoProvider.Version == FhirSpecification.R5) ||
                (types.Any() && !types.Contains(_revinclude.resourceType)))
            {
                return new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>());
            }

            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(_revinclude.searchParameterName, resourceId),
            };

            if (since != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                searchParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
            }

            // We do not use Patient?_revinclude here since it depends on the existence of the parent resource
            using IScoped<ISearchService> search = _searchServiceFactory();
            SearchOptions searchOptions = _searchOptionsFactory.Create(_revinclude.resourceType, searchParameters);
            return await search.Value.SearchAsync(searchOptions, cancellationToken);
        }

        private async Task<SearchResult> SearchCompartmentWithDate(
            string resourceId,
            PartialDateTime start,
            PartialDateTime end,
            PartialDateTime since,
            IReadOnlyList<string> types,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            SearchParameterInfo clinicalDateInfo = _searchParameterDefinitionManager.GetSearchParameter(SearchParameterNames.ClinicalDateUri);
            List<string> dateResourceTypes = types.Any()
                ? clinicalDateInfo.BaseResourceTypes.Intersect(types).ToList()
                : clinicalDateInfo.BaseResourceTypes.ToList();

            if (!dateResourceTypes.Any())
            {
                return new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>());
            }

            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(SearchParameterNames.ResourceType, string.Join(',', dateResourceTypes)),
            };

            if (since != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));
            }

            if (start != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.Date, $"ge{start}"));
            }

            if (end != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.Date, $"le{end}"));
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                searchParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
            }

            using IScoped<ISearchService> search = _searchServiceFactory();
            SearchOptions searchOptions = _searchOptionsFactory.Create(ResourceType.Patient.ToString(), resourceId, null, searchParameters);
            return await search.Value.SearchAsync(searchOptions, cancellationToken);
        }

        private async Task<SearchResult> SearchCompartmentWithoutDate(
            string resourceId,
            string parentPatientId,
            PartialDateTime since,
            IReadOnlyList<string> types,
            string continuationToken,
            EverythingOperationContinuationToken token,
            CancellationToken cancellationToken)
        {
            var nonDateResourceTypes = new List<string>();
            SearchParameterInfo clinicalDateInfo = _searchParameterDefinitionManager.GetSearchParameter(SearchParameterNames.ClinicalDateUri);

            if (_compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> compartmentResourceTypes))
            {
                nonDateResourceTypes = types.Any()
                    ? compartmentResourceTypes.Except(clinicalDateInfo.BaseResourceTypes).Intersect(types).ToList()
                    : compartmentResourceTypes.Except(clinicalDateInfo.BaseResourceTypes).ToList();
            }

            if (!nonDateResourceTypes.Any())
            {
                return new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>());
            }

            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(SearchParameterNames.ResourceType, string.Join(',', nonDateResourceTypes)),
            };

            if (since != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                searchParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
            }

            // If we are processing a "seealso" link, the parent patient that links to it will be in its patient compartment
            if (token.IsProcessingSeeAlsoLink)
            {
                // Add a parameter to prevent this, since we already returned the parent patient resource in a previous call
                searchParameters.Add(Tuple.Create($"{KnownQueryParameterNames.Id}:not", parentPatientId));
            }

            using IScoped<ISearchService> search = _searchServiceFactory();
            SearchOptions searchOptions = _searchOptionsFactory.Create(ResourceType.Patient.ToString(), resourceId, null, searchParameters);
            return await search.Value.SearchAsync(searchOptions, cancellationToken);
        }

        private async Task<SearchResult> SearchCompartment(
            string resourceId,
            string parentPatientId,
            PartialDateTime since,
            IReadOnlyList<string> types,
            string continuationToken,
            EverythingOperationContinuationToken token,
            CancellationToken cancellationToken)
        {
            var searchParameters = new List<Tuple<string, string>>();

            if (since != null)
            {
                searchParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));
            }

            if (types.Any() && _compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> compartmentResourceTypes))
            {
                var filteredTypes = compartmentResourceTypes.Intersect(types).ToList();
                if (!filteredTypes.Any())
                {
                    return new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>());
                }

                searchParameters.Add(Tuple.Create(SearchParameterNames.ResourceType, string.Join(',', filteredTypes)));
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                searchParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
            }

            // If we are processing a "seealso" link, the parent patient that links to it will be in its patient compartment
            if (token.IsProcessingSeeAlsoLink)
            {
                // Add a parameter to prevent this, since we already returned the parent patient resource in a previous call
                searchParameters.Add(Tuple.Create($"{KnownQueryParameterNames.Id}:not", parentPatientId));
            }

            using IScoped<ISearchService> search = _searchServiceFactory();
            SearchOptions searchOptions = _searchOptionsFactory.Create(ResourceType.Patient.ToString(), resourceId, null, searchParameters);
            return await search.Value.SearchAsync(searchOptions, cancellationToken);
        }
    }
}
