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
    internal class TokenSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenSearchParamTableTypeV2Row> _searchParamGenerator;

        internal TokenSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public TokenSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkTokenSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkTokenSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkTokenSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.TokenSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkTokenSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkTokenSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkTokenSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.TokenSearchParam.SystemId.Metadata.Name, searchParam.SystemId);
            FillColumn(newRow, VLatest.TokenSearchParam.Code.Metadata.Name, searchParam.Code);
            FillColumn(newRow, VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, searchParam.CodeOverflow);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenSearchParam.SystemId.Metadata.Name, VLatest.TokenSearchParam.SystemId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenSearchParam.Code.Metadata.Name, VLatest.TokenSearchParam.Code.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenSearchParam.CodeOverflow.Metadata.Name, VLatest.TokenSearchParam.CodeOverflow.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkTokenSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkTokenSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkTokenSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkTokenSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkTokenSearchParamTableTypeV2Row x, BulkTokenSearchParamTableTypeV2Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.Code, y.Code, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.CodeOverflow, y.CodeOverflow, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId, y.SystemId))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkTokenSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.Code?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.SystemId?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
