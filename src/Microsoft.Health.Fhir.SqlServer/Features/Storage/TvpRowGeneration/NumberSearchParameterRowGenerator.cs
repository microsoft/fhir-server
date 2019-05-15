// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class NumberSearchParameterRowGenerator : SearchParameterRowGenerator<NumberSearchValue, V1.NumberSearchParamTableTypeRow>
    {
        private static readonly decimal MinLowValue = GetMinOrMaxSqlDecimalValueForColumn(columnMetadata: V1.NumberSearchParam.LowValue.Metadata, min: true);
        private static readonly decimal MaxHighValue = GetMinOrMaxSqlDecimalValueForColumn(columnMetadata: V1.NumberSearchParam.HighValue.Metadata, min: false);

        public NumberSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        protected override V1.NumberSearchParamTableTypeRow GenerateRow(short searchParamId, SearchParameter searchParameter, NumberSearchValue searchValue)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            return new V1.NumberSearchParamTableTypeRow(
                searchParamId,
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)MinLowValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)MaxHighValue);
        }

        private static decimal GetMinOrMaxSqlDecimalValueForColumn(SqlMetaData columnMetadata, bool min)
        {
            var val = decimal.Parse($"{new string('9', columnMetadata.Precision - columnMetadata.Scale)}.{new string('9', columnMetadata.Scale)}");
            return min ? -val : val;
        }
    }
}
