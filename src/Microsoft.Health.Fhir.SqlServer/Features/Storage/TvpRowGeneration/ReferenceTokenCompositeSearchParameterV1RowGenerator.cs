// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceTokenCompositeSearchParameterV1RowGenerator : CompositeSearchParameterRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), ReferenceTokenCompositeSearchParamTableTypeV2Row>
    {
        private readonly ReferenceSearchParameterV1RowGenerator _referenceV1RowGenerator;
        private readonly TokenSearchParameterV1RowGenerator _tokenRowGenerator;

        public ReferenceTokenCompositeSearchParameterV1RowGenerator(
            SqlServerFhirModel model,
            ReferenceSearchParameterV1RowGenerator referenceV1RowGenerator,
            TokenSearchParameterV1RowGenerator tokenRowGenerator)
            : base(model)
        {
            _referenceV1RowGenerator = referenceV1RowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, out ReferenceTokenCompositeSearchParamTableTypeV2Row row)
        {
            if (_referenceV1RowGenerator.TryGenerateRow(default, searchValue.component1, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new ReferenceTokenCompositeSearchParamTableTypeV2Row(
                    searchParamId,
                    reference1Row.BaseUri,
                    reference1Row.ReferenceResourceTypeId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId,
                    token2Row.Code);

                return true;
            }

            row = default;
            return false;
        }
    }
}
