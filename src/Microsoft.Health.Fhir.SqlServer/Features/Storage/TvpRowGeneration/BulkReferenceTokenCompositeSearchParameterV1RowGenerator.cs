﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkReferenceTokenCompositeSearchParameterV1RowGenerator : BulkCompositeSearchParameterRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), BulkReferenceTokenCompositeSearchParamTableTypeV1Row>
    {
        private readonly BulkReferenceSearchParameterV1RowGenerator _referenceRowGenerator;
        private readonly BulkTokenSearchParameterV1RowGenerator _tokenRowGenerator;

        public BulkReferenceTokenCompositeSearchParameterV1RowGenerator(
            SqlServerFhirModel model,
            BulkReferenceSearchParameterV1RowGenerator referenceRowGenerator,
            BulkTokenSearchParameterV1RowGenerator tokenRowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, out BulkReferenceTokenCompositeSearchParamTableTypeV1Row row)
        {
            if (_referenceRowGenerator.TryGenerateRow(offset, searchParamId, searchValue.component1, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(offset, searchParamId, searchValue.component2, out var token2Row))
            {
                row = new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(
                    offset,
                    searchParamId,
                    reference1Row.BaseUri == null ? string.Empty : reference1Row.BaseUri.ToString(),
                    reference1Row.ReferenceResourceTypeId ?? SqlSearchConstants.NullId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId ?? SqlSearchConstants.NullId,
                    token2Row.Code);

                return true;
            }

            row = default;
            return false;
        }
    }
}
