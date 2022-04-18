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
    internal class NumberSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkNumberSearchParamTableTypeV1Row> _searchParamGenerator;

        internal NumberSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public NumberSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkNumberSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkNumberSearchParamTableTypeV1RowComparer Comparer { get; } = new BulkNumberSearchParamTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.NumberSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkNumberSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkNumberSearchParamTableTypeV1Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkNumberSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.NumberSearchParam.SingleValue.Metadata.Name, searchParam.SingleValue);
            FillColumn(newRow, VLatest.NumberSearchParam.LowValue.Metadata.Name, searchParam.LowValue);
            FillColumn(newRow, VLatest.NumberSearchParam.HighValue.Metadata.Name, searchParam.HighValue);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.NumberSearchParam.SingleValue.Metadata.Name, VLatest.NumberSearchParam.SingleValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.NumberSearchParam.LowValue.Metadata.Name, VLatest.NumberSearchParam.LowValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.NumberSearchParam.HighValue.Metadata.Name, VLatest.NumberSearchParam.HighValue.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkNumberSearchParamTableTypeV1Row> Distinct(IEnumerable<BulkNumberSearchParamTableTypeV1Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkNumberSearchParamTableTypeV1RowComparer : IEqualityComparer<BulkNumberSearchParamTableTypeV1Row>
        {
            public bool Equals(BulkNumberSearchParamTableTypeV1Row x, BulkNumberSearchParamTableTypeV1Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
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

            public int GetHashCode(BulkNumberSearchParamTableTypeV1Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.SingleValue?.GetHashCode() ?? 0;
                hashCode ^= obj.HighValue?.GetHashCode() ?? 0;
                hashCode ^= obj.LowValue?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
