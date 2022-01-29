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
    internal class BulkStringSearchParameterV3RowGenerator : BulkSearchParameterRowGenerator<StringSearchValue, BulkStringSearchParamTableTypeV3Row>, IDisposable
    {
        private readonly int _indexedTextMaxLength = (int)VLatest.StringSearchParam.Text.Metadata.MaxLength;
        private SHA256 _sha256 = SHA256.Create();

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

            var hashText = overflow != null ? overflow : indexedPrefix;
            byte[] computedHash = _sha256.ComputeHash(Encoding.Unicode.GetBytes(hashText));

            row = new BulkStringSearchParamTableTypeV3Row(offset, searchParamId, indexedPrefix, overflow, searchValue.IsMin, searchValue.IsMax, computedHash);

            return true;
        }

        public void Dispose()
        {
            _sha256?.Dispose();
        }
    }
}
