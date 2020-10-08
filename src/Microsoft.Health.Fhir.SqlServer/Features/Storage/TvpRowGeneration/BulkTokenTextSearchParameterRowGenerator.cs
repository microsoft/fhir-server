// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenTextSearchParameterRowGenerator : BulkSearchParameterRowGenerator<TokenSearchValue, VLatest.BulkTokenTextTableTypeRow>
    {
        public BulkTokenTextSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(int id, short searchParamId, TokenSearchValue searchValue, out VLatest.BulkTokenTextTableTypeRow row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            row = new VLatest.BulkTokenTextTableTypeRow(id, searchParamId, searchValue.Text);
            return true;
        }
    }
}
