// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenSearchParamListRowGenerator : MergeSearchParameterRowGenerator<TokenSearchValue, TokenSearchParamListRow>
    {
        // Default hashing of the generated struct can collapse all rows for a resource into one bucket.
        private static readonly IEqualityComparer<TokenSearchParamListRow> RowComparer = EqualityComparer<TokenSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId == right.SystemId &&
                string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow, right.CodeOverflow, StringComparison.Ordinal),
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.SystemId, row.Code, row.CodeOverflow));

        private short _resourceIdSearchParamId;
        private readonly int _indexedCodeMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;

        public TokenSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, TokenSearchValue searchValue, HashSet<TokenSearchParamListRow> results, out TokenSearchParamListRow row)
        {
            // For composite generator contains BulkTokenSearchParameterV1RowGenerator, it is possible to call TryGenerateRow before GenerateRow on this Generator.
            EnsureInitialized();

            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            string indexedPrefix;
            string overflow;
            if (searchValue.Code.Length > _indexedCodeMaxLength)
            {
                // TODO: this truncation can break apart grapheme clusters.
                indexedPrefix = searchValue.Code.Substring(0, _indexedCodeMaxLength);
                overflow = searchValue.Code.Substring(_indexedCodeMaxLength);
            }
            else
            {
                indexedPrefix = searchValue.Code;
                overflow = null;
            }

            row = new TokenSearchParamListRow(
                resourceTypeId,
                resourceSurrogateId,
                searchParamId,
                searchValue.System == null ? null : Model.GetSystemId(searchValue.System),
                indexedPrefix,
                overflow);

            return results == null || results.Add(row);
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
