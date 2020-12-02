// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceSearchParameterV1RowGenerator : SearchParameterRowGenerator<ReferenceSearchValue, ReferenceSearchParamTableTypeV1Row>
    {
        public ReferenceSearchParameterV1RowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, ReferenceSearchValue searchValue, out ReferenceSearchParamTableTypeV1Row row)
        {
            row = new ReferenceSearchParamTableTypeV1Row(
                searchParamId,
                searchValue.BaseUri?.ToString(),
                Model.GetResourceTypeId(searchValue.ResourceType.ToString()),
                searchValue.ResourceId,
                ReferenceResourceVersion: null);

            return true;
        }
    }
}
