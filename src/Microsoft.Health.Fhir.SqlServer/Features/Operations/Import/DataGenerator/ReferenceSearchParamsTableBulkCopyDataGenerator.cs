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
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                newRow[VLatest.ReferenceSearchParam.BaseUri.Metadata.Name] = searchParam.BaseUri;
                newRow[VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name] = searchParam.ReferenceResourceTypeId;
                newRow[VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name] = searchParam.ReferenceResourceId;
                newRow[VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name] = searchParam.ReferenceResourceVersion;

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, VLatest.ReferenceSearchParam.BaseUri.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Type));
        }
    }
}
