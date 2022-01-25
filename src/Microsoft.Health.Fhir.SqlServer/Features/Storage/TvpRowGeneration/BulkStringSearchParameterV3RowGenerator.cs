// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkStringSearchParameterV3RowGenerator : BulkSearchParameterRowGenerator<StringSearchValue, BulkStringSearchParamTableTypeV3Row>
    {
        private readonly int _indexedTextMaxLength = (int)VLatest.StringSearchParam.Text.Metadata.MaxLength;

        public BulkStringSearchParameterV3RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, StringSearchValue searchValue, out BulkStringSearchParamTableTypeV3Row row)
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

            string computedHash;
            using (SHA256 sha256Hash = SHA256.Create())
            {
                var hashText = overflow != null ? overflow : indexedPrefix;
                computedHash = BitConverter.ToString(sha256Hash.ComputeHash(Encoding.Unicode.GetBytes(hashText))).Replace("-", string.Empty, StringComparison.CurrentCultureIgnoreCase);
            }

            row = new BulkStringSearchParamTableTypeV3Row(offset, searchParamId, indexedPrefix, overflow, IsMin: searchValue.IsMin, IsMax: searchValue.IsMax, TextHash: computedHash);
            return true;
        }
    }
}
