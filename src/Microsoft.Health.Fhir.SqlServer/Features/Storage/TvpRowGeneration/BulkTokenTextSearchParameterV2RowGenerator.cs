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
    internal class BulkTokenTextSearchParameterV2RowGenerator : BulkSearchParameterRowGenerator<TokenSearchValue, BulkTokenTextTableTypeV2Row>, IDisposable
    {
        private SHA256 _sha256 = SHA256.Create();

        public BulkTokenTextSearchParameterV2RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, TokenSearchValue searchValue, out BulkTokenTextTableTypeV2Row row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            byte[] computedHash = _sha256.ComputeHash(Encoding.Unicode.GetBytes(searchValue.Text));

            row = new BulkTokenTextTableTypeV2Row(offset, searchParamId, searchValue.Text, computedHash);
            return true;
        }

        public void Dispose()
        {
            _sha256?.Dispose();
        }
    }
}
