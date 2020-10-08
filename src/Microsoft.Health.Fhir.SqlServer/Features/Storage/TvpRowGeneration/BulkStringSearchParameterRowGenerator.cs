// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkStringSearchParameterRowGenerator : BulkSearchParameterRowGenerator<StringSearchValue, VLatest.BulkStringSearchParamTableTypeRow>
    {
        private readonly int _indexedTextMaxLength = (int)VLatest.StringSearchParam.Text.Metadata.MaxLength;

        public BulkStringSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(int id, short searchParamId, StringSearchValue searchValue, out VLatest.BulkStringSearchParamTableTypeRow row)
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

            row = new VLatest.BulkStringSearchParamTableTypeRow(id, searchParamId, indexedPrefix, overflow);
            return true;
        }
    }
}
