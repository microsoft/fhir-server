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
        public static DataTable GenerateResourceTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            ResourceTableBulkCopyDataGenerator generator = new ResourceTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                ResourceTableBulkCopyDataGenerator.FillDataTable(result, resoureType, (resourceId ?? Guid.NewGuid().ToString()) + i.ToString(), startSurrogatedId + i, new byte[10], string.Empty);
            }

            return result;
        }

        public static DataTable GenerateDateTimeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            DateTimeSearchParamsTableBulkCopyDataGenerator generator = new DateTimeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                DateTimeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkDateTimeSearchParamTableTypeV2Row(0, 0, default(DateTimeOffset), default(DateTimeOffset), true, IsMin: true, IsMax: false));
            }

            return result;
        }

        public static DataTable GenerateNumberSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            NumberSearchParamsTableBulkCopyDataGenerator generator = new NumberSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                NumberSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkNumberSearchParamTableTypeV1Row(0, 0, 1, 1, 1));
            }

            return result;
        }

        public static DataTable GenerateQuantitySearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            QuantitySearchParamsTableBulkCopyDataGenerator generator = new QuantitySearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                QuantitySearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkQuantitySearchParamTableTypeV1Row(0, 0, 1, 1, 1, 1, 1));
            }

            return result;
        }

        public static DataTable GenerateReferenceSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            ReferenceSearchParamsTableBulkCopyDataGenerator generator = new ReferenceSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                ReferenceSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkReferenceSearchParamTableTypeV1Row(0, 0, string.Empty, 1, string.Empty, 1));
            }

            return result;
        }

        public static DataTable GenerateReferenceTokenCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator generator = new ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkReferenceTokenCompositeSearchParamTableTypeV2Row(0, 0, string.Empty, 1, string.Empty, 1, 1, string.Empty, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateStringSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            StringSearchParamsTableBulkCopyDataGenerator generator = new StringSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                StringSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkStringSearchParamTableTypeV2Row(0, 0, string.Empty, string.Empty, IsMin: true, IsMax: true));
            }

            return result;
        }

        public static DataTable GenerateTokenDateTimeCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenDateTimeCompositeSearchParamTableTypeV2Row(0, 0, 1, string.Empty, string.Empty, default(DateTimeOffset), default(DateTimeOffset), true));
            }

            return result;
        }

        public static DataTable GenerateTokenNumberNumberCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row(0, 0, 1, string.Empty, 0, 0, 0, 0, 0, 0, true));
            }

            return result;
        }

        public static DataTable GenerateTokenQuantityCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenQuantityCompositeSearchParamTableTypeV1Row(0, 0, 0, string.Empty, 0, 0, 0, 0, 0));
            }

            return result;
        }

        public static DataTable GenerateTokenSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenSearchParamsTableBulkCopyDataGenerator generator = new TokenSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenSearchParamTableTypeV2Row(0, 0, 0, string.Empty, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateTokenStringCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenStringCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenStringCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenStringCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenStringCompositeSearchParamTableTypeV2Row(0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateTokenTextSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenTextSearchParamsTableBulkCopyDataGenerator generator = new TokenTextSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenTextSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenTextTableTypeV1Row(0, 0, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateTokenTokenCompositeSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator generator = new TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkTokenTokenCompositeSearchParamTableTypeV2Row(0, 0, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateUriSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            UriSearchParamsTableBulkCopyDataGenerator generator = new UriSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                UriSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkUriSearchParamTableTypeV1Row(default, 0, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateCompartmentAssignmentTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            CompartmentAssignmentTableBulkCopyDataGenerator generator = new CompartmentAssignmentTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                CompartmentAssignmentTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkCompartmentAssignmentTableTypeV1Row(0, 1, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateResourceWriteClaimTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            ResourceWriteClaimTableBulkCopyDataGenerator generator = new ResourceWriteClaimTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                ResourceWriteClaimTableBulkCopyDataGenerator.FillDataTable(result, startSurrogatedId + i, new BulkResourceWriteClaimTableTypeV1Row(0, 1, string.Empty));
            }

            return result;
        }

        public static DataTable GenerateInValidUriSearchParamsTable(int count, long startSurrogatedId, short resoureType, string resourceId = null)
        {
            UriSearchParamsTableBulkCopyDataGenerator generator = new UriSearchParamsTableBulkCopyDataGenerator();

            DataTable result = generator.GenerateDataTable();

            for (int i = 0; i < count; ++i)
            {
                UriSearchParamsTableBulkCopyDataGenerator.FillDataTable(result, resoureType, startSurrogatedId + i, new BulkUriSearchParamTableTypeV1Row(default, 0, null));
            }

            return result;
        }
    }
}
