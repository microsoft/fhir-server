// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class DateTimeSearchParameterRowGenerator : SearchParameterRowGenerator<DateTimeSearchValue, V1.DateTimeSearchParamTableTypeRow>
    {
        public DateTimeSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        protected override V1.DateTimeSearchParamTableTypeRow GenerateRow(short searchParamId, SearchParameterInfo searchParameter, DateTimeSearchValue searchValue)
        {
            return new V1.DateTimeSearchParamTableTypeRow(
                searchParamId,
                searchValue.Start,
                searchValue.End);
        }
    }
}
