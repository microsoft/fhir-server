// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkReferenceTokenCompositeSearchParameterV2RowGenerator : BulkCompositeSearchParameterRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), BulkReferenceTokenCompositeSearchParamTableTypeV2Row>
    {
        private readonly BulkReferenceSearchParameterV1RowGenerator _referenceRowGenerator;
        private readonly BulkTokenSearchParameterV2RowGenerator _tokenRowGenerator;

        public BulkReferenceTokenCompositeSearchParameterV2RowGenerator(
            SqlServerFhirModel model,
            BulkReferenceSearchParameterV1RowGenerator referenceRowGenerator,
            BulkTokenSearchParameterV2RowGenerator tokenRowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, out BulkReferenceTokenCompositeSearchParamTableTypeV2Row row)
        {
            if (_referenceRowGenerator.TryGenerateRow(offset, searchParamId, searchValue.component1, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(offset, searchParamId, searchValue.component2, out var token2Row))
            {
                row = new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(
                    offset,
                    searchParamId,
                    reference1Row.BaseUri,
                    reference1Row.ReferenceResourceTypeId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId,
                    token2Row.Code,
                    token2Row.CodeOverflow);

                return true;
            }

            row = default;
            return false;
        }
    }
}
