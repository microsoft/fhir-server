// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, ReferenceTokenCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        public ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, ReferenceTokenCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
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
            IEnumerable<ReferenceTokenCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (ReferenceTokenCompositeSearchParamTableTypeV2Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, ReferenceTokenCompositeSearchParamTableTypeV2Row searchParam)
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

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
