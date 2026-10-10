// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ResourceWriteClaimListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, ResourceWriteClaimListRow>
    {
        // Default generated-row hashing can collapse every claim for the same resource version.
        // Keep full-field equality; normalization remains confined to the deduplication key.
        private static readonly IEqualityComparer<ResourceWriteClaimListRow> RowComparer = EqualityComparer<ResourceWriteClaimListRow>.Create(
            (left, right) =>
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.ClaimTypeId == right.ClaimTypeId &&
                string.Equals(left.ClaimValue, right.ClaimValue, StringComparison.Ordinal),
            row => HashCode.Combine(row.ResourceSurrogateId, row.ClaimTypeId, row.ClaimValue));

        private readonly ISqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;

        public ResourceWriteClaimListRowGenerator(ISqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
        }

        public IEnumerable<ResourceWriteClaimListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            foreach (var merge in resources.Where(_ => !_.ResourceWrapper.IsHistory)) // only current
            {
                var resource = merge.ResourceWrapper;

                var resourceMetadata = new ResourceMetadata(
                    resource.CompartmentIndices,
                    resource.SearchIndices?.Where(e => e.SearchParameter.VectorConfig == null).ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                    resource.LastModifiedClaims);

                IReadOnlyCollection<KeyValuePair<string, string>> writeClaims = resourceMetadata.WriteClaims;
                if (writeClaims == null)
                {
                    continue;
                }

                var resultsForDedupping = new HashSet<ResourceWriteClaimListRow>(RowComparer);
                foreach (var claim in writeClaims)
                {
                    if (resultsForDedupping.Add(new ResourceWriteClaimListRow(merge.ResourceWrapper.ResourceSurrogateId, _model.GetClaimTypeId(claim.Key), claim.Value?.ToLowerInvariant())))
                    {
                        yield return new ResourceWriteClaimListRow(merge.ResourceWrapper.ResourceSurrogateId, _model.GetClaimTypeId(claim.Key), claim.Value);
                    }
                }
            }
        }
    }
}
