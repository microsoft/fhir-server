// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class DateTimeSearchParameterRowGenerator : SearchParameterRowGenerator<DateTimeSearchValue, V1.DateTimeSearchParamTableTypeRow>
    {
        public DateTimeSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, DateTimeSearchValue searchValue, out V1.DateTimeSearchParamTableTypeRow row)
        {
            row = new V1.DateTimeSearchParamTableTypeRow(
                searchParamId,
                searchValue.Start,
                searchValue.End);

            return true;
        }
    }
}
