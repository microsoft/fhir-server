// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
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
            List<BulkReferenceTokenCompositeSearchParamTableTypeV1Row> input = new List<BulkReferenceTokenCompositeSearchParamTableTypeV1Row>()
            {
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 1, "test", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test1", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 0, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test1", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 0, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 0, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, "test1"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, null, 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", null, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, null, 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", null, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, null, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, null),

                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 1, "test", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test1", 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 0, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test1", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 0, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 0, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, "test1"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, null, 1, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", null, "test", 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, null, 1, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", null, 1, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, null, "test"),
                new BulkReferenceTokenCompositeSearchParamTableTypeV1Row(0, 0, "test", 1, "test", 1, 1, null),
            };

            Assert.Equal(14, ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
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
            List<BulkTokenDateTimeCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenDateTimeCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", endDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, startDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, endDateTime, false),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, null, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, null, startDateTime, endDateTime, true),

                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", endDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, startDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", startDateTime, endDateTime, false),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, null, "test", startDateTime, endDateTime, true),
                new BulkTokenDateTimeCompositeSearchParamTableTypeV1Row(0, 0, 1, null, startDateTime, endDateTime, true),
            };

            Assert.Equal(9, TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenNumberNumberCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 0, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 0, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 0, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 0, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 0, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 1, false),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, null, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, null, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, null, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, null, true),

                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 0, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 0, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 0, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 0, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 0, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, 1, false),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, 1, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null, 1, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, null, 1, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, null, 1, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, null, 1, true),
                new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1, null, true),
            };

            Assert.Equal(19, TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenQuantityCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenQuantityCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenQuantityCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 0, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 0, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 0, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 0),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, null, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, null, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, null),

                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 0, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 0, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 0, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, 0),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, 1, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null, 1, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, null, 1, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, null, 1),
                new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, 1, 1, 1, null),
            };

            Assert.Equal(16, TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenSearchParamTableTypeV1Row> input = new List<BulkTokenSearchParamTableTypeV1Row>()
            {
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 1, 1, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 0, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, "test1", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, null, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, null, null),

                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 1, 1, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 0, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, "test1", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, null, "test", null),
                new BulkTokenSearchParamTableTypeV1Row(0, 0, 1, null, null),
            };

            Assert.Equal(6, TokenSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenStringCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenStringCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenStringCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test1", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", "test1"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, null, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, null, "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", null),

                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test1", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", "test1"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, null, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, null, "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", null),
            };

            Assert.Equal(10, TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
        }

        [Fact]
        public void GivenListTokenTokenCompositeSearchParams_WhenDinstict_ThenRecordShouldBeDistincted()
        {
            List<BulkTokenTokenCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenTokenCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, "test1"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null),

                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 0, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, "test1"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, null, "test", 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, null, 1, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", null, "test"),
                new BulkTokenTokenCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", 1, null),
            };

            Assert.Equal(10, TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
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
            List<BulkTokenStringCompositeSearchParamTableTypeV1Row> input = new List<BulkTokenStringCompositeSearchParamTableTypeV1Row>()
            {
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "TEST", "TEST"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", "TEST", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", "test", "TEST"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", "Test", "tEst"),

                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 1, 1, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 0, "test", "test", "test"),
                new BulkTokenStringCompositeSearchParamTableTypeV1Row(0, 0, 1, "test1", "test", "test"),
            };

            Assert.Equal(4, TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.Distinct(input).Count());
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
