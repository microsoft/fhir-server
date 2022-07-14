// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenSearchParameterV1RowGenerator : BulkSearchParameterRowGenerator<TokenSearchValue, BulkTokenSearchParamTableTypeV1Row>
    {
        private short _resourceIdSearchParamId;
        private readonly int _indexedCodeMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;

        public BulkTokenSearchParameterV1RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, TokenSearchValue searchValue, out BulkTokenSearchParamTableTypeV1Row row)
        {
            // For composite generator contains BulkTokenSearchParameterV1RowGenerator, it is possible to call TryGenerateRow before GenerateRow on this Generator.
            EnsureInitialized();

            // don't store if the code is empty or if this is the Resource _id parameter. The id is already maintained on the Resource table.
            if (string.IsNullOrWhiteSpace(searchValue.Code) ||
                searchParamId == _resourceIdSearchParamId)
            {
                row = default;
                return false;
            }

            string indexedPrefix;
            string overflow;
            if (searchValue.Code.Length > _indexedCodeMaxLength)
            {
                // TODO: this truncation can break apart grapheme clusters.
                indexedPrefix = searchValue.Code.Substring(0, _indexedCodeMaxLength);
                overflow = searchValue.Code;
            }
            else
            {
                indexedPrefix = searchValue.Code;
                overflow = null;
            }

            row = new BulkTokenSearchParamTableTypeV1Row(
                offset,
                searchParamId,
                searchValue.System == null ? null : Model.GetSystemId(searchValue.System),
                indexedPrefix,
                overflow);

            return true;
        }

        protected override void Initialize() => _resourceIdSearchParamId = Model.GetSearchParamId(SearchParameterNames.IdUri);
    }
}
