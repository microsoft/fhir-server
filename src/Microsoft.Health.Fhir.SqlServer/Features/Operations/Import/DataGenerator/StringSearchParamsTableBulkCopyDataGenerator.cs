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
    internal class StringSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, StringSearchParamTableTypeV1Row> _searchParamGenerator;

        public StringSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, StringSearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.StringSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<StringSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (StringSearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, StringSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.StringSearchParam.Text.Metadata.Name, searchParam.Text);
            FillColumn(newRow, VLatest.StringSearchParam.TextOverflow.Metadata.Name, searchParam.TextOverflow);

            table.Rows.Add(newRow);
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.Text.Metadata.Name, VLatest.StringSearchParam.Text.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.TextOverflow.Metadata.Name, VLatest.StringSearchParam.TextOverflow.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
