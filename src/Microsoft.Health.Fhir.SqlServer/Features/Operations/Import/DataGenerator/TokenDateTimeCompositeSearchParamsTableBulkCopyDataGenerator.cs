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
    internal class TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenDateTimeCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenDateTimeCompositeSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkTokenDateTimeCompositeSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenDateTimeCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenDateTimeCompositeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenDateTimeCompositeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);

            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.CodeOverflow1.Metadata.Name, searchParam.CodeOverflow1);
            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.StartDateTime2.Metadata.Name, searchParam.StartDateTime2.DateTime);
            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.EndDateTime2.Metadata.Name, searchParam.EndDateTime2.DateTime);
            FillColumn(newRow, VLatest.TokenDateTimeCompositeSearchParam.IsLongerThanADay2.Metadata.Name, searchParam.IsLongerThanADay2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.CodeOverflow1.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.CodeOverflow1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.StartDateTime2.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.StartDateTime2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.EndDateTime2.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.EndDateTime2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenDateTimeCompositeSearchParam.IsLongerThanADay2.Metadata.Name, VLatest.TokenDateTimeCompositeSearchParam.IsLongerThanADay2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenDateTimeCompositeSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkTokenDateTimeCompositeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkTokenDateTimeCompositeSearchParamTableTypeV2Row x, BulkTokenDateTimeCompositeSearchParamTableTypeV2Row y)
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

                if (!DateTimeOffset.Equals(x.StartDateTime2, y.StartDateTime2))
                {
                    return false;
                }

                if (!DateTimeOffset.Equals(x.EndDateTime2, y.EndDateTime2))
                {
                    return false;
                }

                if (x.IsLongerThanADay2 != y.IsLongerThanADay2)
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenDateTimeCompositeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId1?.GetHashCode() ?? 0;
                hashCode ^= obj.StartDateTime2.GetHashCode();
                hashCode ^= obj.EndDateTime2.GetHashCode();
                hashCode ^= obj.IsLongerThanADay2.GetHashCode();

                return hashCode.GetHashCode();
            }
        }
    }
}
