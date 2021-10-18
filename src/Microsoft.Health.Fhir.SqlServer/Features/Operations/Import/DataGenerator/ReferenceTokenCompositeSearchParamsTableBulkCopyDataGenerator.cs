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
    internal class ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceTokenCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        internal ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceTokenCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.ReferenceTokenCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkReferenceTokenCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkReferenceTokenCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkReferenceTokenCompositeSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.Name, searchParam.BaseUri1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.Name, searchParam.ReferenceResourceTypeId1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.Name, searchParam.ReferenceResourceId1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.Name, searchParam.ReferenceResourceVersion1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.Name, searchParam.Code2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
