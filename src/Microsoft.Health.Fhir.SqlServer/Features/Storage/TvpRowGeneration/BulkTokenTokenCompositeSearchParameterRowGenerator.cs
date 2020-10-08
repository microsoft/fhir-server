// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenTokenCompositeSearchParameterRowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, TokenSearchValue component2), VLatest.BulkTokenTokenCompositeSearchParamTableTypeRow>
    {
        private readonly BulkTokenSearchParameterRowGenerator _tokenRowGenerator;

        public BulkTokenTokenCompositeSearchParameterRowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterRowGenerator tokenRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(int id, short searchParamId, (TokenSearchValue component1, TokenSearchValue component2) searchValue, out VLatest.BulkTokenTokenCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _tokenRowGenerator.TryGenerateRow(default, default, searchValue.component2, out var token2Row))
            {
                row = new VLatest.BulkTokenTokenCompositeSearchParamTableTypeRow(
                    id,
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
