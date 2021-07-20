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
    internal class ResourceWriteClaimTableBulkCopyDataGenerator : TableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkResourceWriteClaimTableTypeV1Row> _generator;

        internal ResourceWriteClaimTableBulkCopyDataGenerator()
        {
        }

        public ResourceWriteClaimTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkResourceWriteClaimTableTypeV1Row> generator)
        {
            EnsureArg.IsNotNull(generator, nameof(generator));

            _generator = generator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.ResourceWriteClaim.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkResourceWriteClaimTableTypeV1Row> claims = _generator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (var claim in claims)
            {
                FillDataTable(table, input.ResourceSurrogateId, claim);
            }
        }

        internal static void FillDataTable(DataTable table, long resourceSurrogateId, BulkResourceWriteClaimTableTypeV1Row claim)
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
