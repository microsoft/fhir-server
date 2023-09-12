// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    /// <summary>
    /// The Patient $everything operation gets all the information related to a given patient. It returns this
    /// information in up to four different phases, and each result set provides a URL that can be used to get the next
    /// phase of results, if any.
    /// In some cases, a patient resource can have links that point to other patient resources. There are four different
    /// types of links: "seealso", "replaced-by", "replaces" and "refer".
    /// "seealso" links point to another patient resource that contains data about the same person. We follow "seealso"
    /// links and run Patient $everything on them, returning information in phases as we did for the parent patient. We
    /// only do this once, so we do not follow the "seealso" links of a "seealso" link.
    /// </summary>
    public class PatientEverythingService : IPatientEverythingService
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly IUrlResolver _urlResolver;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        private IReadOnlyList<string> _includes = new[] { "general-practitioner", "organization" };
        private readonly (string resourceType, string searchParameterName) _revinclude = new("Device", "patient");

        public PatientEverythingService(
            IModelInfoProvider modelInfoProvider,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchOptionsFactory searchOptionsFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ICompartmentDefinitionManager compartmentDefinitionManager,
            IReferenceSearchValueParser referenceSearchValueParser,
            IResourceDeserializer resourceDeserializer,
            IUrlResolver urlResolver,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _modelInfoProvider = modelInfoProvider;
            _searchServiceFactory = searchServiceFactory;
            _searchOptionsFactory = searchOptionsFactory;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _compartmentDefinitionManager = compartmentDefinitionManager;
            _referenceSearchValueParser = referenceSearchValueParser;
            _resourceDeserializer = resourceDeserializer;
            _urlResolver = urlResolver;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _contextAccessor = contextAccessor;
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
                : EverythingOperationContinuationToken.FromJson(ContinuationTokenConverter.Decode(continuationToken));

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

            // If we are currently processing a "seealso" link, set the resource id to the id of the link.
            resourceId = token.IsProcessingSeeAlsoLink ? token.CurrentSeeAlsoLinkId : resourceId;

            var phase = token.Phase;
            switch (phase)
            {
                // Phase 0 gets the patient and any resources it references directly.
                case 0:
                    searchResult = await SearchIncludes(resourceId, parentPatientId, since, types, token, cancellationToken);

                    if (!searchResult.Results.Any() && string.IsNullOrEmpty(searchResult.ContinuationToken))
                    {
                        phase = 1;
                        goto case 1;
                    }

                    break;

                // Phase 1 gets the resources in the patient's compartment that have date/time values.
                case 1:
                    // If both start and end are null, we can just perform a regular compartment search in Phase 2.
                    if (start == null && end == null)
                    {
                        phase = 2;
                        goto case 2;
                    }

                    searchResult = await SearchCompartmentWithDate(resourceId, start, end, since, types, encodedInternalContinuationToken, cancellationToken);
                    if (!searchResult.Results.Any() && string.IsNullOrEmpty(searchResult.ContinuationToken))
                    {
                        phase = 2;
                        goto case 2;
                    }

                    break;

                // Phase 2 gets the resources in the patient's compartment that do not have date/time values.
                case 2:
                    searchResult = start == null && end == null
                        ? await SearchCompartment(resourceId, parentPatientId, since, types, encodedInternalContinuationToken, token, cancellationToken)
                        : await SearchCompartmentWithoutDate(resourceId, parentPatientId, since, types, encodedInternalContinuationToken, token, cancellationToken);

                    // Starting from FHIR R5, "Devices" are included as part of Compartment Search.
                    // Previous versions of FHIR should still query for "Devices" explicitly.
                    if (_modelInfoProvider.Version < FhirSpecification.R5)
                    {
                        if (!searchResult.Results.Any() && string.IsNullOrEmpty(searchResult.ContinuationToken))
                        {
                            phase = 3;
                            goto case 3;
                        }
                    }

                    break;

                // Phase 3 gets the patient's devices.
                case 3:
                    searchResult = await SearchRevinclude(resourceId, since, types, encodedInternalContinuationToken, cancellationToken);
                    break;
                default:
                    throw new EverythingOperationException(string.Format(Core.Resources.InvalidEverythingOperationPhase, phase), HttpStatusCode.BadRequest);
            }

            string nextContinuationToken;
            if (searchResult.ContinuationToken != null)
            {
                // Keep processing the remaining results for the current phase.
                token.InternalContinuationToken = searchResult.ContinuationToken;

                nextContinuationToken = token.ToJson();
            }
            else if (phase < 2 || (_modelInfoProvider.Version < FhirSpecification.R5 && phase == 2))
            {
                // Starting from FHIR R5, "Devices" are included as part of Compartment Search.
                // No need to run Phase 3 for FHIR R5.

                token.Phase = phase + 1;
                token.InternalContinuationToken = null;

                nextContinuationToken = token.ToJson();
            }
            else
            {
                nextContinuationToken = await CheckForNextSeeAlsoLinkAndSetToken(parentPatientId, token, cancellationToken);

                // If the last phase returned no results and there are links to process
                if (!searchResult.Results.Any() && nextContinuationToken != null)
                {
                    // Run patient $everything on links.
                    return await SearchAsync(parentPatientId, start, end, since, type, ContinuationTokenConverter.Encode(nextContinuationToken), cancellationToken);
                }
            }

            return new SearchResult(searchResult.Results, nextContinuationToken, searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
        }

        private async Task<SearchResult> SearchIncludes(
            string resourceId,
            string parentPatientId,
            PartialDateTime since,
            IReadOnlyList<string> types,
            EverythingOperationContinuationToken token,
            CancellationToken cancellationToken)
        {
            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            var searchResultEntries = new List<SearchResultEntry>();

            // Build the search parameters to add to the query.
            var searchParameters = new List<Tuple<string, string>>
            {
                Tuple.Create(SearchParameterNames.Id, resourceId),
            };

            searchParameters.AddRange(_includes.Select(include => Tuple.Create(SearchParameterNames.Include, $"{ResourceType.Patient}:{include}")));

            // Search for the patient and all the resources it references directly.
            SearchOptions searchOptions = await _searchOptionsFactory.Create(KnownResourceTypes.Patient, searchParameters, cancellationToken: cancellationToken);
            SearchResult searchResult = await search.Value.SearchAsync(searchOptions, cancellationToken);
            searchResultEntries.AddRange(searchResult.Results.Select(x => new SearchResultEntry(x.Resource)));

            // If we are currently processing the parent patient
            if (!token.IsProcessingSeeAlsoLink)
            {
                SearchResultEntry parentPatientResource = searchResultEntries.FirstOrDefault(s =>
                    string.Equals(s.Resource.ResourceTypeName, KnownResourceTypes.Patient, StringComparison.Ordinal));

                // If the parent patient exists in the database
                if (parentPatientResource.Resource != null)
                {
                    CheckForReplacedByLinks(parentPatientId, parentPatientResource.Resource);

                    // Store the version of the parent patient in the token to ensure we fetch the same version when processing "seealso" links.
                    token.ParentPatientVersionId = parentPatientResource.Resource.Version;
                }
            }

            // Filter results by _type.
            if (types.Any())
            {
                searchResultEntries = searchResultEntries.Where(s => types.Contains(s.Resource.ResourceTypeName)).ToList();
            }

            // Filter results by _since.
            if (since != null)
            {
                var sinceDateTimeOffset = since.ToDateTimeOffset();
                searchResultEntries = searchResultEntries.Where(s => s.Resource.LastModified.CompareTo(sinceDateTimeOffset) >= 0).ToList();
            }

            return new SearchResult(searchResultEntries, searchResult.ContinuationToken, searchResult.SortOrder, searchResult.UnsupportedSearchParameters);
        }

        private void CheckForReplacedByLinks(string parentPatientId, ResourceWrapper parentPatientResource)
        {
            List<Patient.LinkComponent> links = ExtractLinksFromParentPatient(parentPatientResource);

            if (links == null)
            {
                return;
            }

            // If a "replaced-by" link is present, it indicates that the patient resource containing this link must no
            // longer be used. The link points forward to another patient resource that must be used in lieu of the
            // resource that contains the "replaced-by" link.
            foreach (Patient.LinkComponent link in links)
            {
                // Regardless of the other links present, we throw an error if we find a "replaced-by" link.
                if (link.Type == Patient.LinkType.ReplacedBy)
                {
                    ReferenceSearchValue referenceSearchValue = _referenceSearchValueParser.Parse(link.Other.Reference);

                    // Ignore RelatedPerson "replaced-by" links, since we can only direct users to run the $everything operation on Patient resources.
                    if (string.Equals(referenceSearchValue.ResourceType, KnownResourceTypes.Patient, StringComparison.Ordinal))
                    {
                        // Specify the url that must be used instead.
                        Uri url = _urlResolver.ResolveOperationResultUrl(OperationsConstants.PatientEverything, referenceSearchValue.ResourceId);

                        // If the prefer header is set to handling=strict
                        if (_contextAccessor.GetIsStrictHandlingEnabled())
                        {
                            throw new EverythingOperationException(
                                string.Format(
                                    Core.Resources.EverythingOperationResourceIrrelevant,
                                    parentPatientId,
                                    referenceSearchValue.ResourceId),
                                HttpStatusCode.MovedPermanently,
                                url.ToString());
                        }
                        else
                        {
                            // If it isn't set to anything, or if it is set to handling=lenient, return an operation outcome within the search bundle.
                            // This will still allow the patient $everything results to be returned.
                            _contextAccessor.RequestContext?.BundleIssues.Add(new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Warning,
                                OperationOutcomeConstants.IssueType.Conflict,
                                string.Format(CultureInfo.InvariantCulture, Core.Resources.EverythingOperationResourceIrrelevant, parentPatientId, referenceSearchValue.ResourceId)));
                        }
                    }
                }
            }
        }

        private List<Patient.LinkComponent> ExtractLinksFromParentPatient(ResourceWrapper parentPatientResource)
        {
            ResourceElement element = _resourceDeserializer.Deserialize(parentPatientResource);
            Patient parentPatient = element.ToPoco<Patient>();

            return parentPatient.Link;
        }

        private async Task<string> CheckForNextSeeAlsoLinkAndSetToken(
            string parentPatientId,
            EverythingOperationContinuationToken token,
            CancellationToken cancellationToken)
        {
            ResourceWrapper parentPatientResource;

            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory.Invoke())
            {
                // Get the version of the parent patient we recorded in the first $everything operation API call.
                parentPatientResource = await store.Value.GetAsync(new ResourceKey(KnownResourceTypes.Patient, parentPatientId, token.ParentPatientVersionId), cancellationToken);
            }

            // If it wasn't found, it means the parent patient doesn't exist in the database.
            if (parentPatientResource == null)
            {
                // There are no links to extract since there is no parent patient resource.
                return null;
            }

            List<Patient.LinkComponent> links = ExtractLinksFromParentPatient(parentPatientResource);

            if (links == null)
            {
                return null;
            }

            var seeAlsoLinkIdsHash = new HashSet<string>();

            // Walk through the links, storing the "seealso" links to a hash set.
            // This will allow us to avoid:
            // 1. Links that aren't "seealso" links
            // 2. Links that don't point to Patient resources
            // 3. Duplicates
            foreach (Patient.LinkComponent link in links)
            {
                ReferenceSearchValue referenceSearchValue = _referenceSearchValueParser.Parse(link.Other.Reference);

                if (link.Type != Patient.LinkType.Seealso)
                {
                    continue;
                }

                // If the link points back to the parent patient
                if (string.Equals(referenceSearchValue.ResourceId, parentPatientId, StringComparison.Ordinal))
                {
                    // Ignore it to avoid running patient $everything on the same patient again.
                    continue;
                }

                // Links can be of type Patient or RelatedPerson. Only follow the links that point to patients.
                // Note: we plan to include RelatedPerson resources in the result set in AB#85142.
                if (string.Equals(referenceSearchValue.ResourceType, KnownResourceTypes.Patient, StringComparison.Ordinal))
                {
                    if (!seeAlsoLinkIdsHash.Contains(referenceSearchValue.ResourceId))
                    {
                        seeAlsoLinkIdsHash.Add(referenceSearchValue.ResourceId);
                    }
                }
            }

            if (seeAlsoLinkIdsHash.Count == 0)
            {
                // The parent patient has links, but no "seealso" patient links.
                return null;
            }

            // Sort the list to ensure that it is in the same order each time we access it.
            var seeAlsoLinkIdsSorted = seeAlsoLinkIdsHash.ToList();
            seeAlsoLinkIdsSorted.Sort();

            // If we haven't processed any "seealso" links yet
            if (token.CurrentSeeAlsoLinkId == null)
            {
                // Then the next "seealso" link we process will be the first in the list.
                token.CurrentSeeAlsoLinkId = seeAlsoLinkIdsSorted.First();
            }
            else
            {
                // Otherwise, the next "seealso" link we process will be the one that follows the current one in the sorted list.
                int indexOfCurrent = seeAlsoLinkIdsSorted.FindIndex(id => id == token.CurrentSeeAlsoLinkId);
                int indexOfNext = indexOfCurrent + 1;

                if (indexOfNext < seeAlsoLinkIdsSorted.Count)
                {
                    token.CurrentSeeAlsoLinkId = seeAlsoLinkIdsSorted[indexOfNext];
                }
                else
                {
                    // We reached the end of the list. There are no more "seealso" links to process.
                    return null;
                }
            }

            // If we reached this point, there is another "seealso" link to process.
            // Reset the phase and internal continuation token so that we can run the $everything operation on the next "seealso" link.
            token.Phase = 0;
            token.InternalContinuationToken = null;

            return token.ToJson();
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
            if ((_modelInfoProvider.Version == FhirSpecification.R5) ||
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
            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            SearchOptions searchOptions = await _searchOptionsFactory.Create(_revinclude.resourceType, searchParameters, cancellationToken: cancellationToken);
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
            SearchParameterInfo clinicalDateInfo = _searchParameterDefinitionManager.GetSearchParameter(SearchParameterNames.ClinicalDateUri.OriginalString);
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

            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            SearchOptions searchOptions = await _searchOptionsFactory.Create(KnownResourceTypes.Patient, resourceId, null, searchParameters, cancellationToken: cancellationToken);
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
            SearchParameterInfo clinicalDateInfo = _searchParameterDefinitionManager.GetSearchParameter(SearchParameterNames.ClinicalDateUri.OriginalString);

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

            CheckForParentPatientDuplicate(parentPatientId, token, searchParameters);

            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            SearchOptions searchOptions = await _searchOptionsFactory.Create(KnownResourceTypes.Patient, resourceId, null, searchParameters, cancellationToken: cancellationToken);
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

            CheckForParentPatientDuplicate(parentPatientId, token, searchParameters);

            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            SearchOptions searchOptions = await _searchOptionsFactory.Create(KnownResourceTypes.Patient, resourceId, null, searchParameters, cancellationToken: cancellationToken);
            return await search.Value.SearchAsync(searchOptions, cancellationToken);
        }

        private static void CheckForParentPatientDuplicate(string parentPatientId, EverythingOperationContinuationToken token, List<Tuple<string, string>> searchParameters)
        {
            // If we are processing a "seealso" link, the parent patient that references it will be in its patient compartment.
            if (token.IsProcessingSeeAlsoLink)
            {
                // Add a parameter to prevent this, since we already returned the parent patient resource in a previous call.
                // Aside: generally, we aim to avoid returning duplicate results in an $everything operation.
                // However, there is a potential for a parent patient's compartment to contain one or more resources that
                // are also present in the patient compartment of one of its "seealso" links. We allow duplicates results
                // to be returned in this scenario.
                searchParameters.Add(Tuple.Create($"{KnownQueryParameterNames.Id}:not", parentPatientId));
            }
        }
    }
}
