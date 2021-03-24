// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenTokenCompositeSearchParameterV1RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, TokenSearchValue component2), BulkTokenTokenCompositeSearchParamTableTypeV1Row>
    {
        private readonly BulkTokenSearchParameterV1RowGenerator _tokenRowGenerator;

        public BulkTokenTokenCompositeSearchParameterV1RowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterV1RowGenerator tokenRowGenerator, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, (TokenSearchValue component1, TokenSearchValue component2) searchValue, out BulkTokenTokenCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _tokenRowGenerator.TryGenerateRow(default, default, searchValue.component2, out var token2Row))
            {
                row = new BulkTokenTokenCompositeSearchParamTableTypeV1Row(
                    offset,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token2Row.SystemId,
                    token2Row.Code);

                return true;
            }

            row = default;
            return false;
        }
    }
}
