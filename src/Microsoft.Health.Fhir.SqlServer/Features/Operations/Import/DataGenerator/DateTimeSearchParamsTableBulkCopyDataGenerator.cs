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
    internal class DateTimeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, DateTimeSearchParamTableTypeV1Row> _searchParamGenerator;

        public DateTimeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, DateTimeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.DateTimeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<DateTimeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (DateTimeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                newRow[VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name] = searchParam.StartDateTime;
                newRow[VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name] = searchParam.EndDateTime;
                newRow[VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name] = searchParam.IsLongerThanADay;

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, VLatest.DateTimeSearchParam.StartDateTime.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, VLatest.DateTimeSearchParam.EndDateTime.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Type));
        }
    }
}
