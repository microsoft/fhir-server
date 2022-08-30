// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using Hl7.FhirPath.Sprache;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class DataGeneratorsTests
    {
        private delegate void AddRow<TR>(DataTable result, short resourceTypeId, long resourceSurrogateId, TR inputRow);

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
        public void GivenListTokenTextSearchParams_WhenDinstict_ThenRecordShouldBeDistinctCaseInsensitive()
        {
            List<BulkTokenTextTableTypeV1Row> input = new List<BulkTokenTextTableTypeV1Row>()
            {
                new BulkTokenTextTableTypeV1Row(0, 1, "test"),
                new BulkTokenTextTableTypeV1Row(0, 1, "Test"),
                new BulkTokenTextTableTypeV1Row(0, 2, "Test"),
                new BulkTokenTextTableTypeV1Row(0, 2, null),
                new BulkTokenTextTableTypeV1Row(0, 3, "Test"),
                new BulkTokenTextTableTypeV1Row(0, 3, string.Empty),
            };

            Assert.Equal(5, TokenTextSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListDateTimeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            DateTimeOffset startDateTime = DateTimeOffset.Now;
            DateTimeOffset endDateTime = DateTimeOffset.Now.AddSeconds(1);
            List<BulkDateTimeSearchParamTableTypeV2Row> input = new List<BulkDateTimeSearchParamTableTypeV2Row>()
            {
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 1, startDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, endDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, startDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, false, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, false, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, true, false),

                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 1, startDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, endDateTime, endDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, startDateTime, true, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, false, true, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, false, true),
                new BulkDateTimeSearchParamTableTypeV2Row(0, 0, startDateTime, endDateTime, true, true, false),
            };

            Assert.Equal(7, DateTimeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListNumberSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkNumberSearchParamTableTypeV1Row> input = new List<BulkNumberSearchParamTableTypeV1Row>()
            {
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 1, 1, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 0, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 0, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, 0),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, null, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, null, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, null),

                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 1, 1, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 0, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 0, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, 0),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, null, 1, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, null, 1),
                new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, null),
            };

            Assert.Equal(8, NumberSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListQuantitySearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkQuantitySearchParamTableTypeV1Row> input = new List<BulkQuantitySearchParamTableTypeV1Row>()
            {
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 1, 1, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 0, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 0, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 0, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 0, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, 0),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, null, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, null, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, null, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, null),

                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 1, 1, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 0, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 0, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 0, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 0, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, 0),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, null, 1, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, null, 1, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, null, 1),
                new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, null),
            };

            Assert.Equal(12, QuantitySearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListReferenceSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkReferenceSearchParamTableTypeV1Row> input = new List<BulkReferenceSearchParamTableTypeV1Row>()
            {
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 1, "test", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test1", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 0, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test1", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, null, 1, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", null, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, null, 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", null),

                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 1, "test", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test1", 1, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 0, "test", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test1", 1),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, null, 1, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", null, "test", 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, null, 0),
                new BulkReferenceSearchParamTableTypeV1Row(0, 0, "test", 1, "test", null),
            };

            Assert.Equal(10, ReferenceSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListReferenceTokenCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkReferenceTokenCompositeSearchParamTableTypeV2Row> input = new List<BulkReferenceTokenCompositeSearchParamTableTypeV2Row>()
            {
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 1, "test", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test1", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 0, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test1", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 0, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 0, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test1", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", "test1"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, null, 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", null, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, null, 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", null, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, null, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, null, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", null),

                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 1, "test", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test1", 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 0, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test1", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 0, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 0, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test1", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", "test1"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, null, 1, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", null, "test", 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, null, 1, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", null, 1, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, null, "test", "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, null, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, "test", 1, "test", 1, 1, "test", null),
            };

            Assert.Equal(16, ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListStringSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkStringSearchParamTableTypeV2Row> input = new List<BulkStringSearchParamTableTypeV2Row>()
            {
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 1, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test1", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test1", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", false, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", true, false),
                new BulkStringSearchParamTableTypeV2Row(0, 0, null, "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", null, true, true),

                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 1, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test1", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test1", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", false, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", true, false),
                new BulkStringSearchParamTableTypeV2Row(0, 0, null, "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", null, true, true),
            };

            Assert.Equal(8, StringSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenDateTimeCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            DateTimeOffset startDateTime = DateTimeOffset.Now;
            DateTimeOffset endDateTime = DateTimeOffset.Now.AddSeconds(1);
            List<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", endDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, startDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, endDateTime, false),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, startDateTime, endDateTime, true),

                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", endDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, startDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", startDateTime, endDateTime, false),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, startDateTime, endDateTime, true),
            };

            Assert.Equal(11, TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListTokenNumberNumberCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 0, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 0, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 0, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 0, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 0, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 1, false),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, null, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, null, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, null, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, null, true),

                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 0, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 0, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 0, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 0, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 0, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, 1, false),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, null, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, null, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, null, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1, null, true),
            };

            Assert.Equal(21, TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListTokenQuantityCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenQuantityCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenQuantityCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 0, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 0, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 0, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 0),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, null, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, null, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, null),

                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 0, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 0, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 0, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, 0),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, null, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, null, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, 1, 1, 1, null),
            };

            Assert.Equal(18, TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListTokenSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenSearchParamTableTypeV2Row> input = new List<BulkTokenSearchParamTableTypeV2Row>()
            {
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 1, 1, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 0, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test1", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", "test1"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, null, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, null, "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", null),

                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 1, 1, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 0, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test1", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", "test1"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, null, "test", "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, null, "test"),
                new BulkTokenSearchParamTableTypeV2Row(0, 0, 1, "test", null),
            };

            Assert.Equal(8, TokenSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListTokenStringCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenStringCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenStringCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test1", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", "test1"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", null),

                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test1", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", "test1"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", null),
            };

            Assert.Equal(12, TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenStringCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListTokenTokenCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenTokenCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenTokenCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test1", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", "test1"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", null),

                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 0, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test1", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", "test1"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, null, "test", "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, null, "test", 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", null, 1, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", null, "test", "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, null, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", 1, "test", null),
            };

            Assert.Equal(14, TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());

            ValidateDataTableData(
                input,
                10000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                20000, // IMPORTANT, should be set to fill the row field with numbers different than any other numbers in the row!
                new TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator(),
                (result, resourceTypeId, resourceSurrogateId, inputRow) =>
                TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resourceTypeId, resourceSurrogateId, inputRow));
        }

        [Fact]
        public void GivenListUriSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkUriSearchParamTableTypeV1Row> input = new List<BulkUriSearchParamTableTypeV1Row>()
            {
                new BulkUriSearchParamTableTypeV1Row(0, 0, "test"),
                new BulkUriSearchParamTableTypeV1Row(0, 1, "test"),
                new BulkUriSearchParamTableTypeV1Row(0, 0, "test1"),
                new BulkUriSearchParamTableTypeV1Row(0, 0, null),

                new BulkUriSearchParamTableTypeV1Row(0, 0, "test"),
                new BulkUriSearchParamTableTypeV1Row(0, 1, "test"),
                new BulkUriSearchParamTableTypeV1Row(0, 0, "test1"),
                new BulkUriSearchParamTableTypeV1Row(0, 0, null),
            };

            Assert.Equal(4, UriSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenCaseModifiedListStringSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkStringSearchParamTableTypeV2Row> input = new List<BulkStringSearchParamTableTypeV2Row>()
            {
                new BulkStringSearchParamTableTypeV2Row(0, 0, "TEST", "TEST", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 1, "TEST", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test1", "TEST", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "Test", "tEst1", true, true),

                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 1, "test", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test1", "test", true, true),
                new BulkStringSearchParamTableTypeV2Row(0, 0, "test", "test1", true, true),
            };

            Assert.Equal(4, StringSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenCaseModifiedListTokenStringCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenStringCompositeSearchParamTableTypeV2Row> input = new List<BulkTokenStringCompositeSearchParamTableTypeV2Row>()
            {
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "TEST", "TEST"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", "TEST", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", "test", "TEST"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", "Test", "tEst"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", "Test", "tEst"),

                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 1, 1, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 0, "test", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test1", "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 1, "test", "test1", "test", "test"),
            };

            Assert.Equal(5, TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
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

        private void ValidateDataTableData<TR, TG>(List<TR> input, short resourceTypeId, long resourceSurrogateId, TG generator, AddRow<TR> fillDataTable)
            where TG : TableBulkCopyDataGenerator
        {
            DataTable result = generator.GenerateDataTable();
            for (int i = 0; i < input.Count; i++)
            {
                TR inputRow = input[i];
                checked
                {
                    fillDataTable(result, (short)(resourceTypeId + i), resourceSurrogateId + i, inputRow);
                }
            }

            Assert.Equal(input.Count, result.Rows.Count);

            PropertyInfo[] columnProperties = typeof(TR).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);

            IEnumerable<string> inCols = columnProperties.Select(x => x.Name);
            IEnumerable<string> resultCols = result.Columns.Cast<DataColumn>().Select(x => x.ColumnName);
            IEnumerable<string> intersectCols = inCols.Intersect(resultCols);
            HashSet<string> inColsOnly = new HashSet<string>(inCols.Except(intersectCols));
            HashSet<string> resultColsOnly = new HashSet<string>(resultCols.Except(intersectCols));
            Assert.True(inColsOnly.SetEquals(new string[] { "Offset" }));
            Assert.True(resultColsOnly.SetEquals(new string[] { "IsHistory", "ResourceTypeId", "ResourceSurrogateId"}));

            for (int i = 0; i < input.Count; i++)
            {
                TR inputRow = input[i];
                DataRow resultRow = result.Rows[i];
                foreach (PropertyInfo propertyInfo in columnProperties)
                {
                    string name = propertyInfo.Name;
                    if (name != "Offset")
                    {
                        object inputValue = propertyInfo.GetValue(inputRow);
                        object resultValue = resultRow[name];
                        if (inputValue == null)
                        {
                            Assert.Equal(typeof(DBNull), resultValue.GetType());
                        }
                        else
                        {
                            if (resultValue.GetType() == typeof(DateTime))
                            {
                                resultValue = (DateTimeOffset)(DateTime)resultValue;
                            }

                            Assert.Equal(inputValue, resultValue);
                        }
                    }
                    else
                    {
                        Assert.Equal(0, propertyInfo.GetValue(inputRow));
                    }
                }

                Assert.Equal(false, resultRow["IsHistory"]);
                Assert.Equal(resourceTypeId + i, (short)resultRow["ResourceTypeId"]);
                Assert.Equal(resourceSurrogateId + i, resultRow["ResourceSurrogateId"]);
            }
        }
    }
}
