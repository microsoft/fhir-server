// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public static class TestBulkDataProvider
    {
        public static DataTable GenerateResourceTable(int count, long startSurrogatedId)
        {
            ResourceTableBulkCopyDataGenerator generator = new ResourceTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, Guid.NewGuid().ToString(), startSurrogatedId + i, new byte[10], string.Empty);
            }

            return result;
        }

        public static DataTable GenerateDateTimeSearchParamsTable(int count, long startSurrogatedId)
        {
            DateTimeSearchParamsTableBulkCopyDataGenerator generator = new DateTimeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new DateTimeSearchParamTableTypeV1Row(0, default(DateTimeOffset), default(DateTimeOffset), true));
            }

            return result;
        }

        public static DataTable GenerateNumberSearchParamsTable(int count, long startSurrogatedId)
        {
            NumberSearchParamsTableBulkCopyDataGenerator generator = new NumberSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new NumberSearchParamTableTypeV1Row(0, 1, 1, 1));
            }

            return result;
        }

        public static DataTable GenerateQuantitySearchParamsTable(int count, long startSurrogatedId)
        {
            QuantitySearchParamsTableBulkCopyDataGenerator generator = new QuantitySearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new QuantitySearchParamTableTypeV1Row(0, 1, 1, 1, 1, 1));
            }

            return result;
        }

        public static DataTable GenerateReferenceSearchParamsTable(int count, long startSurrogatedId)
        {
            ReferenceSearchParamsTableBulkCopyDataGenerator generator = new ReferenceSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new ReferenceSearchParamTableTypeV2Row(0, string.Empty, 1, string.Empty, 1));
            }

            return result;
        }

        public static DataTable GenerateReferenceTokenCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator generator = new ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new ReferenceTokenCompositeSearchParamTableTypeV2Row(0, string.Empty, 1, string.Empty, 1, 1, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateStringSearchParamsTable(int count, long startSurrogatedId)
        {
            StringSearchParamsTableBulkCopyDataGenerator generator = new StringSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new StringSearchParamTableTypeV1Row(0, string.Empty, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateTokenDateTimeCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenDateTimeCompositeSearchParamTableTypeV1Row(0, 1, string.Empty, default(DateTimeOffset), default(DateTimeOffset), true));
            }

            return result;
        }

        public static DataTable GenerateTokenNumberNumberCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 1, null, 0, 0, 0, 0, 0, 0, true));
            }

            return result;
        }

        public static DataTable GenerateTokenQuantityCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, null, 0, 0, 0, 0, 0));
            }

            return result;
        }

        public static DataTable GenerateTokenSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenSearchParamsTableBulkCopyDataGenerator generator = new TokenSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenSearchParamTableTypeV1Row(0, 0, null));
            }

            return result;
        }

        public static DataTable GenerateTokenStringCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenStringCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenStringCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenStringCompositeSearchParamTableTypeV1Row(0, 0, null, null, null));
            }

            return result;
        }

        public static DataTable GenerateTokenTextSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenTextSearchParamsTableBulkCopyDataGenerator generator = new TokenTextSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenTextTableTypeV1Row(0, null));
            }

            return result;
        }

        public static DataTable GenerateTokenTokenCompositeSearchParamsTable(int count, long startSurrogatedId)
        {
            TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new TokenTokenCompositeSearchParamTableTypeV1Row(0, 0, null, 0, null));
            }

            return result;
        }

        public static DataTable GenerateUriSearchParamsTable(int count, long startSurrogatedId)
        {
            UriSearchParamsTableBulkCopyDataGenerator generator = new UriSearchParamsTableBulkCopyDataGenerator(null);

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                generator.FillDataTable(result, 103, startSurrogatedId + i, new UriSearchParamTableTypeV1Row(0, null));
            }

            return result;
        }
    }
}
