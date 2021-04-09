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
    internal class QuantitySearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, QuantitySearchParamTableTypeV1Row> _searchParamGenerator;

        public QuantitySearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, QuantitySearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.QuantitySearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<QuantitySearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (QuantitySearchParamTableTypeV1Row searchParam in searchParams)
            {
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                newRow[VLatest.QuantitySearchParam.SystemId.Metadata.Name] = searchParam.SystemId;
                newRow[VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name] = searchParam.QuantityCodeId;
                newRow[VLatest.QuantitySearchParam.SingleValue.Metadata.Name] = searchParam.SingleValue;
                newRow[VLatest.QuantitySearchParam.LowValue.Metadata.Name] = searchParam.LowValue;
                newRow[VLatest.QuantitySearchParam.HighValue.Metadata.Name] = searchParam.HighValue;

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SystemId.Metadata.Name, VLatest.QuantitySearchParam.SystemId.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SingleValue.Metadata.Name, VLatest.QuantitySearchParam.SingleValue.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.LowValue.Metadata.Name, VLatest.QuantitySearchParam.LowValue.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.HighValue.Metadata.Name, VLatest.QuantitySearchParam.HighValue.Metadata.Type));
        }
    }
}
