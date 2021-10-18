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
    internal class TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        internal TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.TokenNumberNumberCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenNumberNumberCompositeSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.Name, searchParam.SingleValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.Name, searchParam.LowValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.Name, searchParam.HighValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.Name, searchParam.SingleValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.Name, searchParam.LowValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.Name, searchParam.HighValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.Name, searchParam.HasRange);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
