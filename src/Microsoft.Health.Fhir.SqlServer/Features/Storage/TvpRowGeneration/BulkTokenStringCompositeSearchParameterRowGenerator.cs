// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenStringCompositeSearchParameterRowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, StringSearchValue component2), VLatest.BulkTokenStringCompositeSearchParamTableTypeRow>
    {
        private readonly BulkTokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly BulkStringSearchParameterRowGenerator _stringRowGenerator;

        public BulkTokenStringCompositeSearchParameterRowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterRowGenerator tokenRowGenerator, BulkStringSearchParameterRowGenerator stringRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringRowGenerator = stringRowGenerator;
        }

        internal override bool TryGenerateRow(int id, short searchParamId, (TokenSearchValue component1, StringSearchValue component2) searchValue, out VLatest.BulkTokenStringCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _stringRowGenerator.TryGenerateRow(default, default, searchValue.component2, out var string2Row))
            {
                row = new VLatest.BulkTokenStringCompositeSearchParamTableTypeRow(
                    id,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    string2Row.Text,
                    TextOverflow2: string2Row.TextOverflow);

                return true;
            }

            row = default;
            return false;
        }
    }
}
