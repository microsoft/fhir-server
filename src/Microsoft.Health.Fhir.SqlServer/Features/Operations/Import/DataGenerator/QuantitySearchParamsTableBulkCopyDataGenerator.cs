// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class QuantitySearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkQuantitySearchParamTableTypeV1Row> _searchParamGenerator;

        internal QuantitySearchParamsTableBulkCopyDataGenerator()
        {
        }

        public QuantitySearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkQuantitySearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkQuantitySearchParamTableTypeV1RowComparer Comparer { get; } = new BulkQuantitySearchParamTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.QuantitySearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkQuantitySearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkQuantitySearchParamTableTypeV1Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkQuantitySearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.QuantitySearchParam.SystemId.Metadata.Name, searchParam.SystemId);
            FillColumn(newRow, VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, searchParam.QuantityCodeId);
            FillColumn(newRow, VLatest.QuantitySearchParam.SingleValue.Metadata.Name, searchParam.SingleValue);
            FillColumn(newRow, VLatest.QuantitySearchParam.LowValue.Metadata.Name, searchParam.LowValue);
            FillColumn(newRow, VLatest.QuantitySearchParam.HighValue.Metadata.Name, searchParam.HighValue);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SystemId.Metadata.Name, VLatest.QuantitySearchParam.SystemId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.QuantityCodeId.Metadata.Name, VLatest.QuantitySearchParam.QuantityCodeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.SingleValue.Metadata.Name, VLatest.QuantitySearchParam.SingleValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.LowValue.Metadata.Name, VLatest.QuantitySearchParam.LowValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.QuantitySearchParam.HighValue.Metadata.Name, VLatest.QuantitySearchParam.HighValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkQuantitySearchParamTableTypeV1Row> Distinct(IEnumerable<BulkQuantitySearchParamTableTypeV1Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkQuantitySearchParamTableTypeV1RowComparer : IEqualityComparer<BulkQuantitySearchParamTableTypeV1Row>
        {
            public bool Equals(BulkQuantitySearchParamTableTypeV1Row x, BulkQuantitySearchParamTableTypeV1Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId, y.SystemId))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.QuantityCodeId, y.QuantityCodeId))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.SingleValue, y.SingleValue))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.HighValue, y.HighValue))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.LowValue, y.LowValue))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkQuantitySearchParamTableTypeV1Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.SystemId?.GetHashCode() ?? 0;
                hashCode ^= obj.QuantityCodeId?.GetHashCode() ?? 0;
                hashCode ^= obj.SingleValue?.GetHashCode() ?? 0;
                hashCode ^= obj.HighValue?.GetHashCode() ?? 0;
                hashCode ^= obj.LowValue?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
