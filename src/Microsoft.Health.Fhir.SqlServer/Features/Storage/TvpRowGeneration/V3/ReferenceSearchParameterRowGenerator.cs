// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class ReferenceSearchParameterRowGenerator : SearchParameterRowGenerator<ReferenceSearchValue, Schema.Model.V3.ReferenceSearchParamTableTypeRow>
    {
        public ReferenceSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, ReferenceSearchValue searchValue, out Schema.Model.V3.ReferenceSearchParamTableTypeRow row)
        {
            row = new Schema.Model.V3.ReferenceSearchParamTableTypeRow(
                searchParamId,
                searchValue.BaseUri?.ToString(),
                Model.GetResourceTypeId(searchValue.ResourceType.ToString()),
                searchValue.ResourceId,
                ReferenceResourceVersion: null);

            return true;
        }
    }
}
