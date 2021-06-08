// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenSearchParameterV1RowGenerator : SearchParameterRowGenerator<TokenSearchValue, TokenSearchParamTableTypeV1Row>
    {
        private short _resourceIdSearchParamId;

        public TokenSearchParameterV1RowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, TokenSearchValue searchValue, out TokenSearchParamTableTypeV1Row row)
        {
            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            row = new TokenSearchParamTableTypeV1Row(
                searchParamId,
                searchValue.System == null ? null : Model.GetSystemId(searchValue.System),
                searchValue.Code);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
