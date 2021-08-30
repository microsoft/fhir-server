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
    internal class DateTimeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkDateTimeSearchParamTableTypeV1Row> _searchParamGenerator;

        internal DateTimeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public DateTimeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkDateTimeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.DateTimeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkDateTimeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkDateTimeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkDateTimeSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, searchParam.StartDateTime.DateTime);
            FillColumn(newRow, VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, searchParam.EndDateTime.DateTime);
            FillColumn(newRow, VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, searchParam.IsLongerThanADay);

            table.Rows.Add(newRow);
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, VLatest.DateTimeSearchParam.StartDateTime.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, VLatest.DateTimeSearchParam.EndDateTime.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
