// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenSearchParameterRowGenerator : BulkSearchParameterRowGenerator<TokenSearchValue, VLatest.BulkTokenSearchParamTableTypeRow>
    {
        private short _resourceIdSearchParamId;

        public BulkTokenSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(int id, short searchParamId, TokenSearchValue searchValue, out VLatest.BulkTokenSearchParamTableTypeRow row)
        {
            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            row = new VLatest.BulkTokenSearchParamTableTypeRow(
                id,
                searchParamId,
                searchValue.System == null ? (int?)null : Model.GetSystemId(searchValue.System),
                searchValue.Code);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
