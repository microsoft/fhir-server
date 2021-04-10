// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, TokenQuantityCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        public TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, TokenQuantityCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.TokenQuantityCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<TokenQuantityCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (TokenQuantityCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, searchParam.QuantityCodeId2);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, searchParam.SingleValue2);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, searchParam.LowValue2);
                FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, searchParam.HighValue2);

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
