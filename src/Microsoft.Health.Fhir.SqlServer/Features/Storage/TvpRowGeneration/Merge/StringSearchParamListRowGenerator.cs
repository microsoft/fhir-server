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
                indexedPrefix = searchValue.String.Substring(0, _indexedTextMaxLength).ToLowerInvariant();
                overflow = searchValue.String?.ToLowerInvariant();
            }
            else
            {
                indexedPrefix = searchValue.String?.ToLowerInvariant();
                overflow = null;
            }

            row = new StringSearchParamListRow(resourceTypeId, resourceSurrogateId, searchParamId, indexedPrefix, overflow, IsMin: searchValue.IsMin, IsMax: searchValue.IsMax);
            return results == null || results.Add(row);
        }
    }
}
