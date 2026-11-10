// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceSearchParamListRowGenerator : MergeSearchParameterRowGenerator<ReferenceSearchValue, ReferenceSearchParamListRow>
    {
        private static readonly IEqualityComparer<ReferenceSearchParamListRow> RowComparer = EqualityComparer<ReferenceSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                string.Equals(left.BaseUri, right.BaseUri, StringComparison.Ordinal) &&
                left.ReferenceResourceTypeId == right.ReferenceResourceTypeId &&
                string.Equals(left.ReferenceResourceId, right.ReferenceResourceId, StringComparison.Ordinal) &&
                left.ReferenceResourceVersion == right.ReferenceResourceVersion,
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.BaseUri, row.ReferenceResourceTypeId, row.ReferenceResourceId, row.ReferenceResourceVersion));

        public ReferenceSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        /// <summary>
        /// Logs one warning per resource type and search parameter for references whose resource id is longer than the
        /// maximum resource id length. Such ids are indexed truncated, so searches on them, including SMART scope
        /// restrictions, can match a different resource whose id starts with the same characters.
        /// The reference values are not logged because they may contain sensitive data.
        /// </summary>
        /// <param name="resources">The resources being written.</param>
        /// <param name="maxResourceIdLength">The maximum resource id length, which is also the indexed length.</param>
        /// <param name="logger">The logger.</param>
        internal static void LogTruncatedReferences(IReadOnlyList<MergeResourceWrapper> resources, int maxResourceIdLength, ILogger logger)
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
                    foreach (int length in GetReferences(entry.Value).Where(r => r.OverlongResourceIdLength != null).Select(r => r.OverlongResourceIdLength.GetValueOrDefault()))
                    {
                        truncated ??= new Dictionary<(string, Uri), (int, int)>();
                        var key = (merge.ResourceWrapper.ResourceTypeName, entry.SearchParameter.Url);
                        truncated.TryGetValue(key, out (int Count, int MaxLength) current);
                        truncated[key] = (current.Count + 1, Math.Max(current.MaxLength, length));
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
                    "Reference search values with a resource id longer than the maximum resource id length were truncated. ResourceType={ResourceType}, SearchParameter={SearchParameter}, Count={Count}, MaxLength={MaxLength}, IndexedLength={IndexedLength}",
                    item.Key.ResourceType,
                    item.Key.SearchParameter,
                    item.Value.Count,
                    item.Value.MaxLength,
                    maxResourceIdLength);
            }
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceRecordId, short searchParamId, ReferenceSearchValue searchValue, HashSet<ReferenceSearchParamListRow> results, out ReferenceSearchParamListRow row)
        {
            row = new ReferenceSearchParamListRow(
                resourceTypeId,
                resourceRecordId,
                searchParamId,
                searchValue.BaseUri?.ToString(),
                searchValue.ResourceType == null ? null : Model.GetResourceTypeId(searchValue.ResourceType),
                searchValue.ResourceId[..Math.Min(searchValue.ResourceId.Length, Model.ResourceIdPolicy.MaxLength)], // Truncate to fit the column size. TODO: We should separate string references (ref resource type is null) from references to resources. This should be a long term fix.
                ReferenceResourceVersion: null);

            return results == null || results.Add(row);
        }

        private static IEnumerable<ReferenceSearchValue> GetReferences(ISearchValue value) =>
            value switch
            {
                ReferenceSearchValue reference => [reference],
                CompositeSearchValue composite => composite.Components.SelectMany(component => component).OfType<ReferenceSearchValue>(),
                _ => [],
            };
    }
}
