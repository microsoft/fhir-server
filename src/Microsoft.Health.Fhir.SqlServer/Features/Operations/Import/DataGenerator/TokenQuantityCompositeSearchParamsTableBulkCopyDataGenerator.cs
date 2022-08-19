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
    internal class TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenQuantityCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenQuantityCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenQuantityCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenQuantityCompositeSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkTokenQuantityCompositeSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenQuantityCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenQuantityCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenQuantityCompositeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenQuantityCompositeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.CodeOverflow1.Metadata.Name, searchParam.CodeOverflow1);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, searchParam.QuantityCodeId2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, searchParam.SingleValue2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, searchParam.LowValue2);
            FillColumn(newRow, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, searchParam.HighValue2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.CodeOverflow1.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.CodeOverflow1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.QuantityCodeId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.SingleValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.LowValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.Name, VLatest.TokenQuantityCompositeSearchParam.HighValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenQuantityCompositeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkTokenQuantityCompositeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenQuantityCompositeSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkTokenQuantityCompositeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkTokenQuantityCompositeSearchParamTableTypeV2Row x, BulkTokenQuantityCompositeSearchParamTableTypeV2Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.Code1, y.Code1, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.CodeOverflow1, y.CodeOverflow1, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId1, y.SystemId1))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId2, y.SystemId2))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.QuantityCodeId2, y.QuantityCodeId2))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.SingleValue2, y.SingleValue2))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.LowValue2, y.LowValue2))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.HighValue2, y.HighValue2))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenQuantityCompositeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId1?.GetHashCode() ?? 0;
                hashCode ^= obj.SystemId2?.GetHashCode() ?? 0;
                hashCode ^= obj.QuantityCodeId2?.GetHashCode() ?? 0;
                hashCode ^= obj.SingleValue2?.GetHashCode() ?? 0;
                hashCode ^= obj.LowValue2?.GetHashCode() ?? 0;
                hashCode ^= obj.HighValue2?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
