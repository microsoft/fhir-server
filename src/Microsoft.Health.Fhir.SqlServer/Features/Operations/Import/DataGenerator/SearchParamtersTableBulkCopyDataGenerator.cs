// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal abstract class SearchParamtersTableBulkCopyDataGenerator : TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>
    {
        internal static readonly SmallIntColumn ResourceTypeId = new SmallIntColumn("ResourceTypeId");
        internal static readonly BigIntColumn ResourceSurrogateId = new BigIntColumn("ResourceSurrogateId");
        internal static readonly SmallIntColumn SearchParamId = new SmallIntColumn("SearchParamId");
        internal static readonly BitColumn IsHistory = new BitColumn("IsHistory");

        internal override void FillSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.Type));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.Type));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.Type));
            FillSearchParamsSchema(table);
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.Type));
        }

        internal abstract void FillSearchParamsSchema(DataTable table);

        internal static DataRow CreateNewRowWithCommonProperties(DataTable table, short resourceTypeId, long resourceSurrogateId, short searchParamId)
        {
            DataRow newRow = table.NewRow();
            newRow[ResourceTypeId.Metadata.Name] = resourceTypeId;
            newRow[ResourceSurrogateId.Metadata.Name] = resourceSurrogateId;
            newRow[SearchParamId.Metadata.Name] = searchParamId;
            newRow[IsHistory.Metadata.Name] = true;

            return newRow;
        }
    }
}
