// -------------------------------------------------------------------------------------------------
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
    internal class TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenQuantityCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        internal TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenQuantityCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

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
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenQuantityCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenQuantityCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenQuantityCompositeSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, searchParam.QuantityCodeId2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, searchParam.SingleValue2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, searchParam.LowValue2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, searchParam.HighValue2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
