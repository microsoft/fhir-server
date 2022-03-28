﻿// -------------------------------------------------------------------------------------------------
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
    internal class StringSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkStringSearchParamTableTypeV2Row> _searchParamGenerator;

        internal StringSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public StringSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkStringSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkReferenceTokenCompositeSearchParamTableTypeV1RowComparer Comparer { get; } = new BulkReferenceTokenCompositeSearchParamTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.StringSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkStringSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkStringSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkStringSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.StringSearchParam.Text.Metadata.Name, searchParam.Text);
            FillColumn(newRow, VLatest.StringSearchParam.TextOverflow.Metadata.Name, searchParam.TextOverflow);
            FillColumn(newRow, VLatest.StringSearchParam.IsMin.Metadata.Name, searchParam.IsMin);
            FillColumn(newRow, VLatest.StringSearchParam.IsMax.Metadata.Name, searchParam.IsMax);
            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.Text.Metadata.Name, VLatest.StringSearchParam.Text.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.TextOverflow.Metadata.Name, VLatest.StringSearchParam.TextOverflow.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.IsMin.Metadata.Name, VLatest.StringSearchParam.IsMin.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.StringSearchParam.IsMax.Metadata.Name, VLatest.StringSearchParam.IsMax.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkStringSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkStringSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkReferenceTokenCompositeSearchParamTableTypeV1RowComparer : IEqualityComparer<BulkStringSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkStringSearchParamTableTypeV2Row x, BulkStringSearchParamTableTypeV2Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.Equals(x.TextOverflow, y.TextOverflow, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (x.IsMax != y.IsMax)
                {
                    return false;
                }

                if (x.IsMin != y.IsMin)
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkStringSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Text?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
                hashCode ^= obj.TextOverflow?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
                hashCode ^= obj.IsMin.GetHashCode();
                hashCode ^= obj.IsMax.GetHashCode();

                return hashCode.GetHashCode();
            }
        }
    }
}
