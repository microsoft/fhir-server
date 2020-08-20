// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class StringSearchParameterRowGenerator : SearchParameterRowGenerator<StringSearchValue, Schema.Model.V3.StringSearchParamTableTypeRow>
    {
        private readonly int _indexedTextMaxLength = (int)Schema.Model.V3.StringSearchParam.Text.Metadata.MaxLength;

        public StringSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, StringSearchValue searchValue, out Schema.Model.V3.StringSearchParamTableTypeRow row)
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

            row = new Schema.Model.V3.StringSearchParamTableTypeRow(searchParamId, indexedPrefix, overflow);
            return true;
        }
    }
}
