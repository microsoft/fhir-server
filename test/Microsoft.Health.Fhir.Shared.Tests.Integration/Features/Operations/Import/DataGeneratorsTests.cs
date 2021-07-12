// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class DataGeneratorsTests
    {
        [Fact]
        public void GivenDateTimeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateDateTimeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.DateTimeSearchParam, table);
        }

        [Fact]
        public void GivenNumberSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateNumberSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.NumberSearchParam, table);
        }

        [Fact]
        public void GivenQuantitySearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateQuantitySearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.QuantitySearchParam, table);
        }

        [Fact]
        public void GivenReferenceSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateReferenceSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.ReferenceSearchParam, table);
        }

        [Fact]
        public void GivenReferenceTokenCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateReferenceTokenCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.ReferenceTokenCompositeSearchParam, table);
        }

        [Fact]
        public void GivenStringSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateStringSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.StringSearchParam, table);
        }

        [Fact]
        public void GivenTokenDateTimeCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenDateTimeCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenDateTimeCompositeSearchParam, table);
        }

        [Fact]
        public void GivenTokenNumberNumberCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenNumberNumberCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenNumberNumberCompositeSearchParam, table);
        }

        [Fact]
        public void GivenTokenQuantityCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenQuantityCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenQuantityCompositeSearchParam, table);
        }

        [Fact]
        public void GivenTokenSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenSearchParam, table);
        }

        [Fact]
        public void GivenTokenStringCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenStringCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenStringCompositeSearchParam, table);
        }

        [Fact]
        public void GivenTokenTextSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenTextSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenText, table);
        }

        [Fact]
        public void GivenTokenTokenCompositeSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateTokenTokenCompositeSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.TokenTokenCompositeSearchParam, table);
        }

        [Fact]
        public void GivenUriSearchParamsRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateUriSearchParamsTable(1, 1000, 103);
            ValidataDataTable(VLatest.UriSearchParam, table);
        }

        [Fact]
        public void GivenCompartmentAssignmentRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateCompartmentAssignmentTable(1, 1000, 103);
            ValidataDataTable(VLatest.CompartmentAssignment, table);
        }

        [Fact]
        public void GivenResourceWriteClaimRecords_WhenGeneratorData_ThenValidDataTableShouldBeReturned()
        {
            DataTable table = TestBulkDataProvider.GenerateResourceWriteClaimTable(1, 1000, 103);
            ValidataDataTable(VLatest.ResourceWriteClaim, table);
        }

        private void ValidataDataTable<T>(T tableDefination, DataTable dataTable)
        {
            Dictionary<string, string> realColumnRecords = new Dictionary<string, string>();
            foreach (DataColumn c in dataTable.Columns)
            {
                realColumnRecords[c.ColumnName] = c.DataType.ToString();
            }

            var columnFields = tableDefination.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.FieldType.IsAssignableTo(typeof(Column))).ToArray();
            Assert.Equal(columnFields.Length, realColumnRecords.Count);
            Assert.Equal(columnFields.Length, dataTable.Rows[0].ItemArray.Length);

            foreach (FieldInfo field in columnFields)
            {
                Column column = (Column)field.GetValue(tableDefination);
                Assert.Equal(realColumnRecords[column.Metadata.Name], column.Metadata.SqlDbType.GetGeneralType().ToString());
            }
        }
    }
}
