﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenStringCompositeSearchParameterV1RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, StringSearchValue component2), BulkTokenStringCompositeSearchParamTableTypeV1Row>
    {
        private readonly BulkTokenSearchParameterV1RowGenerator _tokenRowGenerator;
        private readonly BulkStringSearchParameterV1RowGenerator _stringV1RowGenerator;

        public BulkTokenStringCompositeSearchParameterV1RowGenerator(
            SqlServerFhirModel model,
            BulkTokenSearchParameterV1RowGenerator tokenRowGenerator,
            BulkStringSearchParameterV1RowGenerator stringV1RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringV1RowGenerator = stringV1RowGenerator;
        }

        internal override bool TryGenerateRow(
            int offset,
            short searchParamId,
            (TokenSearchValue component1, StringSearchValue component2) searchValue,
            out BulkTokenStringCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _stringV1RowGenerator.TryGenerateRow(default, default, searchValue.component2, out var string2Row))
            {
                row = new BulkTokenStringCompositeSearchParamTableTypeV1Row(
                    offset,
                    searchParamId,
                    token1Row.SystemId ?? SqlSearchConstants.NullId,
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
