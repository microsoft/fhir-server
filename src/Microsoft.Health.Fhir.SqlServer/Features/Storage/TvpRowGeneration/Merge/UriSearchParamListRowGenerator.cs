// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParamListRowGenerator : MergeSearchParameterRowGenerator<UriSearchValue, UriSearchParamListRow>
    {
        /// <summary>
        /// The number of URI characters stored in, and compared by, the URI search index.
        /// </summary>
        internal static readonly int IndexedUriMaxLength = (int)VLatest.UriSearchParam.Uri.Metadata.MaxLength;

        private static readonly IEqualityComparer<UriSearchParamListRow> RowComparer = EqualityComparer<UriSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                string.Equals(left.Uri, right.Uri, StringComparison.Ordinal),
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.Uri));

        public UriSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        /// <summary>
        /// Finds a URI search parameter for which the resource has two different values that are identical in their first
        /// <see cref="IndexedUriMaxLength"/> characters. The index can't tell such values apart, so the resource must be rejected.
        /// Only values of at least <see cref="IndexedUriMaxLength"/> characters can collide.
        /// </summary>
        /// <param name="resource">The resource being written.</param>
        /// <returns>The code of the first colliding search parameter, or <c>null</c> if there is none.</returns>
        internal static string FindCollidingUriSearchParameter(ResourceWrapper resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            if (resource.SearchIndices == null)
            {
                return null;
            }

            Dictionary<(Uri SearchParameter, string IndexedValue), string> seen = null;
            foreach (SearchIndexEntry entry in resource.SearchIndices)
            {
                if (entry.Value is not UriSearchValue uriValue || uriValue.Uri.Length < IndexedUriMaxLength)
                {
                    continue;
                }

                seen ??= new Dictionary<(Uri, string), string>();
                var key = (entry.SearchParameter.Url, uriValue.Uri[..IndexedUriMaxLength]);
                if (!seen.TryGetValue(key, out string firstValue))
                {
                    seen[key] = uriValue.Uri;
                }
                else if (!string.Equals(firstValue, uriValue.Uri, StringComparison.Ordinal))
                {
                    return entry.SearchParameter.Code ?? entry.SearchParameter.Url?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Logs one warning per resource type and search parameter for URI values longer than the indexed length.
        /// Such values are stored truncated, so searches on them can match resources whose URI shares the indexed prefix.
        /// The URI values are not logged because they may contain sensitive data.
        /// </summary>
        /// <param name="resources">The resources being written.</param>
        /// <param name="logger">The logger.</param>
        internal static void LogTruncatedUris(IReadOnlyList<MergeResourceWrapper> resources, ILogger logger)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));
            EnsureArg.IsNotNull(logger, nameof(logger));

            if (!logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            Dictionary<(string ResourceType, Uri SearchParameter), (int Count, int MaxLength)> truncated = null;

            foreach (MergeResourceWrapper merge in resources)
            {
                if (merge.ResourceWrapper.IsHistory || merge.ResourceWrapper.SearchIndices == null)
                {
                    continue;
                }

                foreach (SearchIndexEntry entry in merge.ResourceWrapper.SearchIndices)
                {
                    if (entry.Value is UriSearchValue uriValue && uriValue.Uri.Length > IndexedUriMaxLength)
                    {
                        truncated ??= new Dictionary<(string, Uri), (int, int)>();
                        var key = (merge.ResourceWrapper.ResourceTypeName, entry.SearchParameter.Url);
                        truncated.TryGetValue(key, out (int Count, int MaxLength) current);
                        truncated[key] = (current.Count + 1, Math.Max(current.MaxLength, uriValue.Uri.Length));
                    }
                }
            }

            if (truncated == null)
            {
                return;
            }

            foreach (KeyValuePair<(string ResourceType, Uri SearchParameter), (int Count, int MaxLength)> item in truncated)
            {
                logger.LogWarning(
                    "URI search values longer than the indexed length were truncated. ResourceType={ResourceType}, SearchParameter={SearchParameter}, Count={Count}, MaxLength={MaxLength}, IndexedLength={IndexedLength}",
                    item.Key.ResourceType,
                    item.Key.SearchParameter,
                    item.Value.Count,
                    item.Value.MaxLength,
                    IndexedUriMaxLength);
            }
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, UriSearchValue searchValue, HashSet<UriSearchParamListRow> results, out UriSearchParamListRow row)
        {
            // Truncate explicitly to what the column stores, so that values differing only past the indexed length are
            // de-duplicated here instead of colliding on the table type's primary key in SQL. New writes with such values
            // are rejected (see FindCollidingUriSearchParameter); this keeps reindexing of already stored resources working.
            string uri = searchValue.Uri.Length > IndexedUriMaxLength ? searchValue.Uri[..IndexedUriMaxLength] : searchValue.Uri;

            row = new UriSearchParamListRow(resourceTypeId, resourceSurrogateId, searchParamId, uri);
            return results == null || results.Add(row);
        }
    }
}
