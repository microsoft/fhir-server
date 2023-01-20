// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class StringSearchParamListRowGenerator : MergeSearchParameterRowGenerator<StringSearchValue, StringSearchParamListRow>
    {
        private readonly int _indexedTextMaxLength = (int)VLatest.StringSearchParam.Text.Metadata.MaxLength;

        public StringSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, StringSearchValue searchValue, HashSet<StringSearchParamListRow> results, out StringSearchParamListRow row)
        {
            string indexedPrefix;
            string overflow;
            if (searchValue.String.Length > _indexedTextMaxLength)
            {
                // TODO: this truncation can break apart grapheme clusters.
                indexedPrefix = searchValue.String.Substring(0, _indexedTextMaxLength);
                overflow = searchValue.String;
            }
            else
            {
                indexedPrefix = searchValue.String;
                overflow = null;
            }

            // There are 2 rows created: one with input values as-is to save in store, and other with lower case values for deduplication.
            // As-is one is needed to support "exact" match in store even though columns are conflated as case insensitive
            // Lower case one is needed to dedup in code using default comparers.
            // Same logic is used in other similar cases.
            row = new StringSearchParamListRow(resourceTypeId, resourceSurrogateId, searchParamId, indexedPrefix, overflow, IsMin: searchValue.IsMin, IsMax: searchValue.IsMax);
            return results == null || results.Add(new StringSearchParamListRow(resourceTypeId, resourceSurrogateId, searchParamId, indexedPrefix?.ToLowerInvariant(), overflow?.ToLowerInvariant(), IsMin: searchValue.IsMin, IsMax: searchValue.IsMax));
        }
    }
}
