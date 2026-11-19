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
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Compares the package with persisted SearchParameter resources and their searchable statuses.
    /// </summary>
    public sealed class SearchParameterPackageInstallVerifier : IFhirPackageInstallVerifier
    {
        private const int MaxUrlLength = 128;
        private const int BatchSize = 100;
        private static readonly FhirJsonParser Parser = new FhirJsonParser();

        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterStatusManager _statusManager;
        private readonly ISearchParameterDefinitionManager _definitionManager;
        private readonly IModelInfoProvider _modelInfoProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterPackageInstallVerifier"/> class.
        /// </summary>
        /// <param name="searchServiceFactory">Searches persisted SearchParameter resources.</param>
        /// <param name="searchParameterOperations">Refreshes the local SearchParameter cache.</param>
        /// <param name="statusManager">Reads persisted SearchParameter statuses.</param>
        /// <param name="definitionManager">Reads the local SearchParameter definitions.</param>
        /// <param name="modelInfoProvider">Provides the FHIR model.</param>
        public SearchParameterPackageInstallVerifier(
            IScopeProvider<ISearchService> searchServiceFactory,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterStatusManager statusManager,
            ISearchParameterDefinitionManager definitionManager,
            IModelInfoProvider modelInfoProvider)
        {
            _searchServiceFactory = EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _statusManager = EnsureArg.IsNotNull(statusManager, nameof(statusManager));
            _definitionManager = EnsureArg.IsNotNull(definitionManager, nameof(definitionManager));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
        }

        /// <inheritdoc />
        public async Task<FhirPackageInstallInspection> InspectAsync(FhirPackage package, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(package, nameof(package));
            var candidates = new Dictionary<string, (FhirPackageEntry Entry, SearchParameter Resource)>(StringComparer.Ordinal);

            foreach (FhirPackageEntry entry in package.ResourceEntries.Where(e => e.ResourceType == KnownResourceTypes.SearchParameter))
            {
                SearchParameter resource = ParseEntry(entry);

                if (string.IsNullOrWhiteSpace(resource.Url) || resource.Url.Length > MaxUrlLength || !Uri.TryCreate(resource.Url, UriKind.Absolute, out _))
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterIssue, entry.Path, resource.Url, string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterInvalidUrl, MaxUrlLength)));
                }

                if (!candidates.TryAdd(resource.Url, (entry, resource)))
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.Duplicate,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterIssue, entry.Path, resource.Url, Core.Resources.FhirPackageSearchParameterDuplicateUrl));
                }
            }

            if (candidates.Count == 0)
            {
                return new FhirPackageInstallInspection(Array.Empty<string>(), Array.Empty<FhirPackageEntry>(), null);
            }

            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
            Dictionary<string, SearchParameter> persisted = await GetResourcesAsync(candidates.Keys.ToArray(), cancellationToken);
            Dictionary<string, ResourceSearchParameterStatus> statuses = (await _statusManager.GetAllSearchParameterStatuses(cancellationToken))
                .ToDictionary(status => status.Uri.OriginalString, StringComparer.Ordinal);

            var missing = new List<FhirPackageEntry>();
            DateTimeOffset? lastUpdated = null;
            foreach (KeyValuePair<string, (FhirPackageEntry Entry, SearchParameter Resource)> candidate in candidates)
            {
                bool hasResource = persisted.TryGetValue(candidate.Key, out SearchParameter existing);
                bool hasStatus = statuses.TryGetValue(candidate.Key, out ResourceSearchParameterStatus status);
                if (!hasResource)
                {
                    if (hasStatus && status.Status != SearchParameterStatus.Deleted)
                    {
                        throw Conflict(candidate.Key, "a live status exists without a matching resource");
                    }

                    missing.Add(candidate.Value.Entry);
                    continue;
                }

                if (!Matches(candidate.Value.Resource, existing))
                {
                    throw Conflict(candidate.Key, "the persisted resource differs from the package");
                }

                if (!hasStatus || status.Status != SearchParameterStatus.Enabled || status.IsPartiallySupported)
                {
                    throw Conflict(candidate.Key, "the persisted status is not fully enabled");
                }

                if (_searchParameterOperations.SearchParamLastUpdated < status.LastUpdated ||
                    !_definitionManager.TryGetSearchParameter(candidate.Key, out SearchParameterInfo info) ||
                    info.SearchParameterStatus != SearchParameterStatus.Enabled ||
                    !info.IsSearchable ||
                    info.IsPartiallySupported)
                {
                    throw new FhirPackageWriteConflictException(
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterCacheNotReady, candidate.Key),
                        isRetryable: true);
                }

                if (!lastUpdated.HasValue || status.LastUpdated > lastUpdated.Value)
                {
                    lastUpdated = status.LastUpdated;
                }
            }

            return new FhirPackageInstallInspection(candidates.Keys.ToArray(), missing, lastUpdated);
        }

        private static SearchParameter ParseEntry(FhirPackageEntry entry)
        {
            try
            {
                return Parser.Parse<SearchParameter>(entry.Json);
            }
            catch (FormatException ex)
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageEntryInvalid, entry.Path, KnownResourceTypes.SearchParameter, ex.Message));
            }
        }

        private async Task<Dictionary<string, SearchParameter>> GetResourcesAsync(string[] urls, CancellationToken cancellationToken)
        {
            var expected = new HashSet<string>(urls, StringComparer.Ordinal);
            var resources = new Dictionary<string, SearchParameter>(StringComparer.Ordinal);
            using var search = _searchServiceFactory.Invoke();

            for (int i = 0; i < urls.Length; i += BatchSize)
            {
                string urlParameter = string.Join(",", urls.Skip(i).Take(BatchSize).Select(url => url.EscapeSearchParameterValue()));
                string continuationToken = null;
                do
                {
                    var parameters = new List<Tuple<string, string>>
                    {
                        Tuple.Create("url", urlParameter),
                        Tuple.Create(KnownQueryParameterNames.Count, BatchSize.ToString(CultureInfo.InvariantCulture)),
                    };
                    if (!string.IsNullOrEmpty(continuationToken))
                    {
                        parameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(continuationToken)));
                    }

                    SearchResult result = await search.Value.SearchAsync(KnownResourceTypes.SearchParameter, parameters, cancellationToken);
                    if (result == null || result.UnsupportedSearchParameters.Count > 0)
                    {
                        throw new InvalidOperationException("Unable to verify package SearchParameters using the URL search.");
                    }

                    foreach (SearchResultEntry entry in result.Results)
                    {
                        if (entry.Resource?.RawResource == null)
                        {
                            throw new InvalidOperationException("A SearchParameter search result did not contain a resource.");
                        }

                        SearchParameter resource = entry.Resource.RawResource.ToITypedElement(_modelInfoProvider).ToPoco<SearchParameter>();
                        if (resource.Url != null && expected.Contains(resource.Url) && !resources.TryAdd(resource.Url, resource))
                        {
                            throw Conflict(resource.Url, "more than one persisted resource has this URL");
                        }
                    }

                    continuationToken = result.ContinuationToken;
                }
                while (!string.IsNullOrEmpty(continuationToken));
            }

            return resources;
        }

        private static bool Matches(SearchParameter expected, SearchParameter existing)
        {
            if (string.IsNullOrWhiteSpace(expected.Id))
            {
                existing.Id = null;
            }

            RemoveServerMetadata(expected);
            RemoveServerMetadata(existing);
            return expected.IsExactly(existing);
        }

        private static void RemoveServerMetadata(SearchParameter resource)
        {
            if (resource.Meta != null)
            {
                resource.Meta.VersionId = null;
                resource.Meta.LastUpdated = null;
                if (resource.Meta.IsExactly(new Meta()))
                {
                    resource.Meta = null;
                }
            }
        }

        private static FhirPackageWriteConflictException Conflict(string url, string detail) =>
            new FhirPackageWriteConflictException(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageExistingSearchParameterConflict, url, detail),
                isRetryable: false);
    }
}
