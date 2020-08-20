// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V4
{
    internal class TokenSearchParameterRowGenerator : SearchParameterRowGenerator<TokenSearchValue, Schema.Model.V4.TokenSearchParamTableTypeRow>
    {
        private short _resourceIdSearchParamId;

        public TokenSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, TokenSearchValue searchValue, out Schema.Model.V4.TokenSearchParamTableTypeRow row)
        {
            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            row = new Schema.Model.V4.TokenSearchParamTableTypeRow(
                searchParamId,
                searchValue.System == null ? (int?)null : Model.GetSystemId(searchValue.System),
                searchValue.Code);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
