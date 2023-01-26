// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class ResourceTableBulkCopyDataGenerator : TableBulkCopyDataGenerator
    {
        private const string ImportMethod = "PUT";

        internal override string TableName
        {
            get
            {
                return VLatest.Resource.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            FillDataTable(table, input.ResourceTypeId, input.Resource.ResourceId, input.ResourceSurrogateId, input.CompressedRawData, input.Resource.SearchParameterHash);
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, string resourceId, long resourceSurrogateId, byte[] data, string searchParameterHash)
        {
            DataRow newRow = table.NewRow();

            FillColumn(newRow, VLatest.Resource.ResourceTypeId.Metadata.Name, resourceTypeId);
            FillColumn(newRow, VLatest.Resource.ResourceId.Metadata.Name, resourceId);
            FillColumn(newRow, VLatest.Resource.Version.Metadata.Name, 0);
            FillColumn(newRow, VLatest.Resource.IsHistory.Metadata.Name, false);
            FillColumn(newRow, VLatest.Resource.ResourceSurrogateId.Metadata.Name, resourceSurrogateId);
            FillColumn(newRow, VLatest.Resource.IsDeleted.Metadata.Name, false);
            FillColumn(newRow, VLatest.Resource.RequestMethod.Metadata.Name, ImportMethod);
            FillColumn(newRow, VLatest.Resource.RawResource.Metadata.Name, data);
            FillColumn(newRow, VLatest.Resource.IsRawResourceMetaSet.Metadata.Name, true);
            FillColumn(newRow, VLatest.Resource.SearchParamHash.Metadata.Name, searchParameterHash);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceTypeId.Metadata.Name, VLatest.Resource.ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceId.Metadata.Name, VLatest.Resource.ResourceId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.Version.Metadata.Name, VLatest.Resource.Version.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsHistory.Metadata.Name, VLatest.Resource.IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceSurrogateId.Metadata.Name, VLatest.Resource.ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsDeleted.Metadata.Name, VLatest.Resource.IsDeleted.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.RequestMethod.Metadata.Name, VLatest.Resource.RequestMethod.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.RawResource.Metadata.Name, VLatest.Resource.RawResource.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsRawResourceMetaSet.Metadata.Name, VLatest.Resource.IsRawResourceMetaSet.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.SearchParamHash.Metadata.Name, VLatest.Resource.SearchParamHash.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
