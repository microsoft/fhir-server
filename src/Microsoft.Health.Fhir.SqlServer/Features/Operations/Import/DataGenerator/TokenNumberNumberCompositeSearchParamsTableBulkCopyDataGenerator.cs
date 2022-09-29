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
    internal class TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenNumberNumberCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenNumberNumberCompositeSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkTokenNumberNumberCompositeSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenNumberNumberCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.CodeOverflow1.Metadata.Name, searchParam.CodeOverflow1);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.Name, searchParam.SingleValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.Name, searchParam.LowValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.Name, searchParam.HighValue2);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.Name, searchParam.SingleValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.Name, searchParam.LowValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.Name, searchParam.HighValue3);
            FillColumn(newRow, VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.Name, searchParam.HasRange);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.LowValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HighValue2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.SingleValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.LowValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HighValue3.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.HasRange.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenNumberNumberCompositeSearchParam.CodeOverflow1.Metadata.Name, VLatest.TokenNumberNumberCompositeSearchParam.CodeOverflow1.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenNumberNumberCompositeSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row x, BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row y)
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

                if (!EqualityComparer<decimal?>.Default.Equals(x.SingleValue3, y.SingleValue3))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.LowValue3, y.LowValue3))
                {
                    return false;
                }

                if (!EqualityComparer<decimal?>.Default.Equals(x.HighValue3, y.HighValue3))
                {
                    return false;
                }

                if (x.HasRange != y.HasRange)
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenNumberNumberCompositeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId1?.GetHashCode() ?? 0;
                hashCode ^= obj.SingleValue2?.GetHashCode() ?? 0;
                hashCode ^= obj.LowValue2?.GetHashCode() ?? 0;
                hashCode ^= obj.HighValue2?.GetHashCode() ?? 0;
                hashCode ^= obj.SingleValue3?.GetHashCode() ?? 0;
                hashCode ^= obj.LowValue3?.GetHashCode() ?? 0;
                hashCode ^= obj.HighValue3?.GetHashCode() ?? 0;
                hashCode ^= obj.HasRange.GetHashCode();

                return hashCode.GetHashCode();
            }
        }
    }
}
