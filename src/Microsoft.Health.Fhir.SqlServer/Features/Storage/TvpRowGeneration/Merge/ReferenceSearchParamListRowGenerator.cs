// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
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

        private readonly SqlReferenceStorageValidator _referenceStorageValidator;

        public ReferenceSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap, SqlReferenceStorageValidator referenceStorageValidator)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _referenceStorageValidator = EnsureArg.IsNotNull(referenceStorageValidator, nameof(referenceStorageValidator));
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceRecordId, short searchParamId, ReferenceSearchValue searchValue, HashSet<ReferenceSearchParamListRow> results, out ReferenceSearchParamListRow row)
        {
            // Reject rather than shorten: a shortened id or base could match a different reference in search and authorization.
            _referenceStorageValidator.EnsureRepresentable(searchValue);

            row = new ReferenceSearchParamListRow(
                resourceTypeId,
                resourceRecordId,
                searchParamId,
                searchValue.BaseUri?.ToString(),
                searchValue.ResourceType == null ? null : Model.GetResourceTypeId(searchValue.ResourceType),
                searchValue.ResourceId,
                ReferenceResourceVersion: null);

            return results == null || results.Add(row);
        }
    }
}
