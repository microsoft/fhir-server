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
    internal class ReferenceSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, ReferenceSearchParamTableTypeV2Row> _searchParamGenerator;

        public ReferenceSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, ReferenceSearchParamTableTypeV2Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.ReferenceSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<ReferenceSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (ReferenceSearchParamTableTypeV2Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, ReferenceSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, searchParam.BaseUri);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, searchParam.ReferenceResourceTypeId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, searchParam.ReferenceResourceId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name, searchParam.ReferenceResourceVersion);

            table.Rows.Add(newRow);
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, VLatest.ReferenceSearchParam.BaseUri.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
