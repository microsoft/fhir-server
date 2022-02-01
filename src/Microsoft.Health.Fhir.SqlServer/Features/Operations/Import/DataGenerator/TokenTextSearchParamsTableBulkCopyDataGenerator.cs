// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class TokenTextSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTextTableTypeV1Row> _searchParamGenerator;

        internal TokenTextSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenTextSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTextTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenTextTableTypeV1RowComparer Comparer { get; } = new BulkTokenTextTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenText.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenTextTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenTextTableTypeV1Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenTextTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenText.Text.Metadata.Name, searchParam.Text);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenText.Text.Metadata.Name, VLatest.TokenText.Text.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenTextTableTypeV1Row> Distinct(IEnumerable<BulkTokenTextTableTypeV1Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenTextTableTypeV1RowComparer : IEqualityComparer<BulkTokenTextTableTypeV1Row>
        {
            public bool Equals(BulkTokenTextTableTypeV1Row x, BulkTokenTextTableTypeV1Row y)
            {
                if (x.SearchParamId == y.SearchParamId && string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            public int GetHashCode(BulkTokenTextTableTypeV1Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode() ^ (string.IsNullOrEmpty(obj.Text) ? 0 : obj.Text.GetHashCode(StringComparison.OrdinalIgnoreCase));
                return hashCode.GetHashCode();
            }
        }
    }
}
