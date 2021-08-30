// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkIdentifierOfTypeSearchParameterV1RowGenerator : BulkSearchParameterRowGenerator<IdentifierOfTypeSearchValue, BulkIdentifierSearchParamTableTypeV1Row>
    {
        private short _resourceIdSearchParamId;

        public BulkIdentifierOfTypeSearchParameterV1RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
              : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, IdentifierOfTypeSearchValue searchValue, out BulkIdentifierSearchParamTableTypeV1Row row)
        {
            EnsureInitialized();

            // don't store if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            row = new BulkIdentifierSearchParamTableTypeV1Row(
                offset,
                searchParamId,
                searchValue.System == null ? null : Model.GetSystemId(searchValue.System),
                searchValue.Code,
                searchValue.Value);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
