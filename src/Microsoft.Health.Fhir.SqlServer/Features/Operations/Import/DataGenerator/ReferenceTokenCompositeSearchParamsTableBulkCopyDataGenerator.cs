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
    internal class ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceTokenCompositeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public ReferenceTokenCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceTokenCompositeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkReferenceTokenCompositeSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkReferenceTokenCompositeSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.ReferenceTokenCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkReferenceTokenCompositeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkReferenceTokenCompositeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkReferenceTokenCompositeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.Name, searchParam.BaseUri1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.Name, searchParam.ReferenceResourceTypeId1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.Name, searchParam.ReferenceResourceId1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.Name, searchParam.ReferenceResourceVersion1);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.Name, searchParam.SystemId2);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.Name, searchParam.Code2);
            FillColumn(newRow, VLatest.ReferenceTokenCompositeSearchParam.CodeOverflow2.Metadata.Name, searchParam.CodeOverflow2);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceTypeId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceVersion1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.SystemId2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.Code2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceTokenCompositeSearchParam.CodeOverflow2.Metadata.Name, VLatest.ReferenceTokenCompositeSearchParam.CodeOverflow2.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkReferenceTokenCompositeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkReferenceTokenCompositeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkReferenceTokenCompositeSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkReferenceTokenCompositeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkReferenceTokenCompositeSearchParamTableTypeV2Row x, BulkReferenceTokenCompositeSearchParamTableTypeV2Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.BaseUri1, y.BaseUri1, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.ReferenceResourceId1, y.ReferenceResourceId1, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<short?>.Default.Equals(x.ReferenceResourceTypeId1, y.ReferenceResourceTypeId1))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.ReferenceResourceVersion1, y.ReferenceResourceVersion1))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.SystemId2, y.SystemId2))
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

                return true;
            }

            public int GetHashCode(BulkReferenceTokenCompositeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.BaseUri1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.ReferenceResourceId1?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.ReferenceResourceTypeId1?.GetHashCode() ?? 0;
                hashCode ^= obj.ReferenceResourceVersion1?.GetHashCode() ?? 0;
                hashCode ^= obj.SystemId2?.GetHashCode() ?? 0;
                hashCode ^= obj.Code2?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.CodeOverflow2?.GetHashCode(StringComparison.Ordinal) ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
