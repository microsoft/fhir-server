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
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTextTableTypeV2Row> _searchParamGenerator;

        internal TokenTextSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenTextSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTextTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenTextTableTypeV2RowComparer Comparer { get; } = new BulkTokenTextTableTypeV2RowComparer();

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

            IEnumerable<BulkTokenTextTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenTextTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenTextTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenText.Text.Metadata.Name, searchParam.Text);
            FillColumn(newRow, VLatest.TokenText.TextHash.Metadata.Name, searchParam.TextHash);

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
            table.Columns.Add(new DataColumn(VLatest.TokenText.TextHash.Metadata.Name, VLatest.TokenText.TextHash.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenTextTableTypeV2Row> Distinct(IEnumerable<BulkTokenTextTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenTextTableTypeV2RowComparer : IEqualityComparer<BulkTokenTextTableTypeV2Row>
        {
            public bool Equals(BulkTokenTextTableTypeV2Row x, BulkTokenTextTableTypeV2Row y)
            {
                if (x.SearchParamId == y.SearchParamId && string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            public int GetHashCode(BulkTokenTextTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode() ^ (string.IsNullOrEmpty(obj.Text) ? 0 : obj.Text.GetHashCode(StringComparison.OrdinalIgnoreCase));
                return hashCode.GetHashCode();
            }
        }
    }
}
