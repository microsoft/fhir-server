// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker.SearchParamGenerator
{
    public class DateSearchParamGenerator : ISearchParamGenerator
    {
        private ModelProvider _modelProvider;

        public DateSearchParamGenerator(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider;
        }

        public string TableName => "dbo.DateTimeSearchParam";

        public DataTable CreateDataTable()
        {
            DataTable table = new DataTable("DataTable");
            DataColumn column;

            column = new DataColumn();
            column.DataType = typeof(short);
            column.ColumnName = "ResourceTypeId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(long);
            column.ColumnName = "ResourceSurrogateId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(short);
            column.ColumnName = "SearchParamId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(DateTime);
            column.ColumnName = "StartDateTime";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(DateTime);
            column.ColumnName = "EndDateTime";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsLongerThanADay";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsHistory";
            column.ReadOnly = true;
            table.Columns.Add(column);

            return table;
        }

        public DataRow GenerateDataRow(DataTable table, BulkCopySearchParamWrapper searchParam)
        {
            DateTimeSearchValue searchValue = (DateTimeSearchValue)searchParam.SearchIndexEntry.Value;

            DataRow row = table.NewRow();
            row["ResourceTypeId"] = _modelProvider.ResourceTypeMapping[searchParam.Resource.InstanceType];
            row["ResourceSurrogateId"] = searchParam.SurrogateId;
            row["SearchParamId"] = _modelProvider.SearchParamTypeMapping.ContainsKey(searchParam.SearchIndexEntry.SearchParameter.Url) ? _modelProvider.SearchParamTypeMapping[searchParam.SearchIndexEntry.SearchParameter.Url] : 0;
            row["IsHistory"] = false;
            FillInRow(row, searchValue);

            return row;
        }

        public static void FillInRow(DataRow row, DateTimeSearchValue searchValue, string index = "")
        {
            row["StartDateTime" + index] = searchValue.Start.DateTime;
            row["EndDateTime" + index] = searchValue.End.DateTime;
            row["IsLongerThanADay" + index] = (searchValue.Start - searchValue.End).Ticks > TimeSpan.TicksPerDay;
        }
    }
}
