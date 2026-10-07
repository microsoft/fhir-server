// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

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

        private readonly int _maxLength = (int)VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.MaxLength;

        public ReferenceSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceRecordId, short searchParamId, ReferenceSearchValue searchValue, HashSet<ReferenceSearchParamListRow> results, out ReferenceSearchParamListRow row)
        {
            row = new ReferenceSearchParamListRow(
                resourceTypeId,
                resourceRecordId,
                searchParamId,
                searchValue.BaseUri?.ToString(),
                searchValue.ResourceType == null ? null : Model.GetResourceTypeId(searchValue.ResourceType),
                searchValue.ResourceId[..Math.Min(searchValue.ResourceId.Length, _maxLength)], // Truncate to fit the column size. TODO: We should separate string references (ref resource type is null) from references to resources. This should be a long term fix.
                ReferenceResourceVersion: null);

            return results == null || results.Add(row);
        }
    }
}
