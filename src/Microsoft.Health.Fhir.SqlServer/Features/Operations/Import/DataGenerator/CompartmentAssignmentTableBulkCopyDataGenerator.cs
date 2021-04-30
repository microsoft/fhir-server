// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class CompartmentAssignmentTableBulkCopyDataGenerator : TableBulkCopyDataGenerator<SqlBulkCopyDataWrapper>
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, CompartmentAssignmentTableTypeV1Row> _generator;

        public CompartmentAssignmentTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, CompartmentAssignmentTableTypeV1Row> generator)
        {
            _generator = generator;
        }

        internal override string TableName => VLatest.CompartmentAssignment.TableName;

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            foreach (var rowData in _generator.GenerateRows(input.Metadata))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, rowData);
            }
        }

        internal void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, CompartmentAssignmentTableTypeV1Row rowData)
        {
            DataRow newRow = table.NewRow();

            FillColumn(newRow, VLatest.CompartmentAssignment.ResourceTypeId.Metadata.Name, resourceTypeId);
            FillColumn(newRow, VLatest.CompartmentAssignment.ResourceSurrogateId.Metadata.Name, resourceSurrogateId);
            FillColumn(newRow, VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, rowData.CompartmentTypeId);
            FillColumn(newRow, VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, rowData.ReferenceResourceId);
            FillColumn(newRow, VLatest.CompartmentAssignment.IsHistory.Metadata.Name, false);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.CompartmentAssignment.ResourceTypeId.Metadata.Name, VLatest.CompartmentAssignment.ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.CompartmentAssignment.ResourceSurrogateId.Metadata.Name, VLatest.CompartmentAssignment.ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.Name, VLatest.CompartmentAssignment.CompartmentTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.Name, VLatest.CompartmentAssignment.ReferenceResourceId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.CompartmentAssignment.IsHistory.Metadata.Name, VLatest.CompartmentAssignment.IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
