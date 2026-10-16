// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceTokenCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), ReferenceTokenCompositeSearchParamListRow>
    {
        private static readonly IEqualityComparer<ReferenceTokenCompositeSearchParamListRow> RowComparer = EqualityComparer<ReferenceTokenCompositeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                string.Equals(left.BaseUri1, right.BaseUri1, StringComparison.Ordinal) &&
                left.ReferenceResourceTypeId1 == right.ReferenceResourceTypeId1 &&
                string.Equals(left.ReferenceResourceId1, right.ReferenceResourceId1, StringComparison.Ordinal) &&
                left.ReferenceResourceVersion1 == right.ReferenceResourceVersion1 &&
                left.SystemId2 == right.SystemId2 &&
                string.Equals(left.Code2, right.Code2, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow2, right.CodeOverflow2, StringComparison.Ordinal),
            row =>
            {
                var hash = default(HashCode);
                hash.Add(row.ResourceTypeId);
                hash.Add(row.ResourceSurrogateId);
                hash.Add(row.SearchParamId);
                hash.Add(row.BaseUri1);
                hash.Add(row.ReferenceResourceTypeId1);
                hash.Add(row.ReferenceResourceId1);
                hash.Add(row.ReferenceResourceVersion1);
                hash.Add(row.SystemId2);
                hash.Add(row.Code2);
                hash.Add(row.CodeOverflow2);
                return hash.ToHashCode();
            });

        private readonly ReferenceSearchParamListRowGenerator _referenceRowGenerator;
        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;

        public ReferenceTokenCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            ReferenceSearchParamListRowGenerator referenceRowGenerator,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, HashSet<ReferenceTokenCompositeSearchParamListRow> results, out ReferenceTokenCompositeSearchParamListRow row)
        {
            // Dedupping hashset is defined in MergeSearchParameterRowGenerator which has access to all processed resources
            // and knows definition of composite row, so dedupping is happening on composite row itself not on its components.
            // Intentionally sending dedpinning results = null into component methods, as there is nothing to deduplicate on.
            // This pattern is implemented in all composite row generators.
            if (_referenceRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.component1, null, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.component2, null, out var token2Row))
            {
                row = new ReferenceTokenCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    reference1Row.BaseUri,
                    reference1Row.ReferenceResourceTypeId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId,
                    token2Row.Code,
                    token2Row.CodeOverflow);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
