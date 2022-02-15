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
    internal class TokenStringCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenStringCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        internal TokenStringCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenStringCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenStringCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenStringCompositeSearchParamTableTypeV1RowComparer Comparer { get; } = new BulkTokenStringCompositeSearchParamTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenStringCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenStringCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenStringCompositeSearchParamTableTypeV1Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenStringCompositeSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.Text2.Metadata.Name, searchParam.Text2);
            FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.Name, searchParam.TextOverflow2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenStringCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.Text2.Metadata.Name, VLatest.TokenStringCompositeSearchParam.Text2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.Name, VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenStringCompositeSearchParamTableTypeV1Row> Distinct(IEnumerable<BulkTokenStringCompositeSearchParamTableTypeV1Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenStringCompositeSearchParamTableTypeV1RowComparer : IEqualityComparer<BulkTokenStringCompositeSearchParamTableTypeV1Row>
        {
            public bool Equals(BulkTokenStringCompositeSearchParamTableTypeV1Row x, BulkTokenStringCompositeSearchParamTableTypeV1Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.Code1, y.Code1, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId1, y.SystemId1))
                {
                    return false;
                }

                if (!string.Equals(x.Text2, y.Text2, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.TextOverflow2, y.TextOverflow2, StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenStringCompositeSearchParamTableTypeV1Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId1?.GetHashCode() ?? 0;
                hashCode ^= obj.Text2?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.TextOverflow2?.GetHashCode(StringComparison.Ordinal) ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
