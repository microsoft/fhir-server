﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenDateTimeCompositeSearchParameterV2RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, DateTimeSearchValue component2),
        BulkTokenDateTimeCompositeSearchParamTableTypeV2Row>
    {
        private readonly BulkTokenSearchParameterV2RowGenerator _tokenRowGenerator;
        private readonly BulkDateTimeSearchParameterV2RowGenerator _dateTimeV2RowGenerator;

        public BulkTokenDateTimeCompositeSearchParameterV2RowGenerator(
            SqlServerFhirModel model,
            BulkTokenSearchParameterV2RowGenerator tokenRowGenerator,
            BulkDateTimeSearchParameterV2RowGenerator dateTimeV2RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _dateTimeV2RowGenerator = dateTimeV2RowGenerator;
        }

        internal override bool TryGenerateRow(
            int offset,
            short searchParamId,
            (TokenSearchValue component1, DateTimeSearchValue component2) searchValue,
            out BulkTokenDateTimeCompositeSearchParamTableTypeV2Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(offset, default, searchValue.component1, out var token1Row) &&
                _dateTimeV2RowGenerator.TryGenerateRow(offset, default, searchValue.component2, out var token2Row))
            {
                row = new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(
                    offset,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token2Row.StartDateTime,
                    token2Row.EndDateTime,
                    token2Row.IsLongerThanADay);

                return true;
            }

            row = default;
            return false;
        }
    }
}
