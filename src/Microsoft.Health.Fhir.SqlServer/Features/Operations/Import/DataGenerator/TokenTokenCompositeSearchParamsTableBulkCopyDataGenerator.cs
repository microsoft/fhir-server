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
    internal class TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTokenCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenTokenCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenTokenCompositeSearchParamTableTypeV2RowRowComparer Comparer { get; } = new BulkTokenTokenCompositeSearchParamTableTypeV2RowRowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenTokenCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenTokenCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenTokenCompositeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenTokenCompositeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.CodeOverflow1.Metadata.Name, searchParam.CodeOverflow1);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.Name, searchParam.Code2);
            FillColumn(newRow, VLatest.TokenTokenCompositeSearchParam.CodeOverflow2.Metadata.Name, searchParam.CodeOverflow2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.CodeOverflow1.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.CodeOverflow1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.CodeOverflow2.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.CodeOverflow2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenTokenCompositeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkTokenTokenCompositeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenTokenCompositeSearchParamTableTypeV2RowRowComparer : IEqualityComparer<BulkTokenTokenCompositeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkTokenTokenCompositeSearchParamTableTypeV2Row x, BulkTokenTokenCompositeSearchParamTableTypeV2Row y)
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

                if (!string.Equals(x.Code2, y.Code2, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.CodeOverflow2, y.CodeOverflow2, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId2, y.SystemId2))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenTokenCompositeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId1?.GetHashCode() ?? 0;
                hashCode ^= obj.Code2?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow2?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId2?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
