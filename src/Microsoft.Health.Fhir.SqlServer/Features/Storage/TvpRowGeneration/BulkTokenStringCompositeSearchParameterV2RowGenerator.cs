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
    internal class BulkTokenStringCompositeSearchParameterV2RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, StringSearchValue component2),
        BulkTokenStringCompositeSearchParamTableTypeV2Row>, IDisposable
    {
        private readonly BulkTokenSearchParameterV2RowGenerator _tokenRowGenerator;
        private readonly BulkStringSearchParameterV2RowGenerator _stringV2RowGenerator;
        private SHA256 _sha256 = SHA256.Create();

        public BulkTokenStringCompositeSearchParameterV2RowGenerator(
            SqlServerFhirModel model,
            BulkTokenSearchParameterV2RowGenerator tokenRowGenerator,
            BulkStringSearchParameterV2RowGenerator stringV2RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringV2RowGenerator = stringV2RowGenerator;
        }

        internal override bool TryGenerateRow(
            int offset,
            short searchParamId,
            (TokenSearchValue component1, StringSearchValue component2) searchValue,
            out BulkTokenStringCompositeSearchParamTableTypeV2Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _stringV2RowGenerator.TryGenerateRow(default, default, searchValue.component2, out var string2Row))
            {
                var hashText = string2Row.TextOverflow != null ? string2Row.TextOverflow : string2Row.Text;
                byte[] computedHash = _sha256.ComputeHash(Encoding.Unicode.GetBytes(hashText));

                row = new BulkTokenStringCompositeSearchParamTableTypeV2Row(
                    offset,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    string2Row.Text,
                    TextOverflow2: string2Row.TextOverflow,
                    TextHash2: computedHash);

                return true;
            }

            row = default;
            return false;
        }

        public void Dispose()
        {
            _sha256?.Dispose();
        }
    }
}
