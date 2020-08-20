// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class TokenTextSearchParameterRowGenerator : SearchParameterRowGenerator<TokenSearchValue, Schema.Model.V3.TokenTextTableTypeRow>
    {
        public TokenTextSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, TokenSearchValue searchValue, out Schema.Model.V3.TokenTextTableTypeRow row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            row = new Schema.Model.V3.TokenTextTableTypeRow(searchParamId, searchValue.Text);
            return true;
        }
    }
}
