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
    internal class ResourceWriteClaimTableBulkCopyDataGenerator : TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, ResourceWriteClaimTableTypeV1Row> _generator;

        public ResourceWriteClaimTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, ResourceWriteClaimTableTypeV1Row> generator)
        {
            _generator = generator;
        }

        internal override string TableName => VLatest.ResourceWriteClaim.TableName;

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<ResourceWriteClaimTableTypeV1Row> claims = _generator.GenerateRows(input.Metadata);

            if (claims != null)
            {
                foreach (var claim in _generator.GenerateRows(input.Metadata))
                {
                    FillDataTable(table, input.ResourceSurrogateId, claim);
                }
            }
        }

        internal static void FillDataTable(DataTable table, long resourceSurrogateId, ResourceWriteClaimTableTypeV1Row claim)
        {
            DataRow newRow = table.NewRow();

            FillColumn(newRow, VLatest.ResourceWriteClaim.ResourceSurrogateId.Metadata.Name, resourceSurrogateId);
            FillColumn(newRow, VLatest.ResourceWriteClaim.ClaimTypeId.Metadata.Name, claim.ClaimTypeId);
            FillColumn(newRow, VLatest.ResourceWriteClaim.ClaimValue.Metadata.Name, claim.ClaimValue);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.ResourceWriteClaim.ResourceSurrogateId.Metadata.Name, VLatest.ResourceWriteClaim.ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ResourceWriteClaim.ClaimTypeId.Metadata.Name, VLatest.ResourceWriteClaim.ClaimTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ResourceWriteClaim.ClaimValue.Metadata.Name, VLatest.ResourceWriteClaim.ClaimValue.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
