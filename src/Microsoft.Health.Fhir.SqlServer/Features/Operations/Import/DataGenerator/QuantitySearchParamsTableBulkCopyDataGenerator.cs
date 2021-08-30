﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class QuantitySearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkQuantitySearchParamTableTypeV1Row> _searchParamGenerator;

        internal QuantitySearchParamsTableBulkCopyDataGenerator()
        {
        }

        public QuantitySearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkQuantitySearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

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
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkQuantitySearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkQuantitySearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkQuantitySearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.QuantitySearchParam.SystemId.Metadata.Name, searchParam.SystemId);
            FillColumn(newRow, VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, searchParam.QuantityCodeId);
            FillColumn(newRow, VLatest.QuantitySearchParam.SingleValue.Metadata.Name, searchParam.SingleValue);
            FillColumn(newRow, VLatest.QuantitySearchParam.LowValue.Metadata.Name, searchParam.LowValue);
            FillColumn(newRow, VLatest.QuantitySearchParam.HighValue.Metadata.Name, searchParam.HighValue);

            table.Rows.Add(newRow);
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SystemId.Metadata.Name, VLatest.QuantitySearchParam.SystemId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, VLatest.QuantitySearchParam.QuantityCodeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SingleValue.Metadata.Name, VLatest.QuantitySearchParam.SingleValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.LowValue.Metadata.Name, VLatest.QuantitySearchParam.LowValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.HighValue.Metadata.Name, VLatest.QuantitySearchParam.HighValue.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
