// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenDateTimeCompositeSearchParameterRowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, DateTimeSearchValue component2), VLatest.BulkTokenDateTimeCompositeSearchParamTableTypeRow>
    {
        private readonly BulkTokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly BulkDateTimeSearchParameterRowGenerator _dateTimeRowGenerator;

        public BulkTokenDateTimeCompositeSearchParameterRowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterRowGenerator tokenRowGenerator, BulkDateTimeSearchParameterRowGenerator dateTimeRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _dateTimeRowGenerator = dateTimeRowGenerator;
        }

        internal override bool TryGenerateRow(int id, short searchParamId, (TokenSearchValue component1, DateTimeSearchValue component2) searchValue, out VLatest.BulkTokenDateTimeCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(id, default, searchValue.component1, out var token1Row) &&
                _dateTimeRowGenerator.TryGenerateRow(id, default, searchValue.component2, out var token2Row))
            {
                row = new VLatest.BulkTokenDateTimeCompositeSearchParamTableTypeRow(
                    id,
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
