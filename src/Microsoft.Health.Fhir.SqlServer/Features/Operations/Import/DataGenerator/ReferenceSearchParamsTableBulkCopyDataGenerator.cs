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
    internal class ReferenceSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceSearchParamTableTypeV1Row> _searchParamGenerator;

        internal ReferenceSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public ReferenceSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReferenceSearchParamTableTypeV1Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkReferenceSearchParamTableTypeV1RowComparer Comparer { get; } = new BulkReferenceSearchParamTableTypeV1RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.ReferenceSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkReferenceSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkReferenceSearchParamTableTypeV1Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkReferenceSearchParamTableTypeV1Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, searchParam.BaseUri);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, searchParam.ReferenceResourceTypeId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, searchParam.ReferenceResourceId);
            FillColumn(newRow, VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name, searchParam.ReferenceResourceVersion);

            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.BaseUri.Metadata.Name, VLatest.ReferenceSearchParam.BaseUri.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.Name, VLatest.ReferenceSearchParam.ReferenceResourceVersion.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkReferenceSearchParamTableTypeV1Row> Distinct(IEnumerable<BulkReferenceSearchParamTableTypeV1Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkReferenceSearchParamTableTypeV1RowComparer : IEqualityComparer<BulkReferenceSearchParamTableTypeV1Row>
        {
            public bool Equals(BulkReferenceSearchParamTableTypeV1Row x, BulkReferenceSearchParamTableTypeV1Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!string.Equals(x.BaseUri, y.BaseUri, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(x.ReferenceResourceId, y.ReferenceResourceId, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!EqualityComparer<short?>.Default.Equals(x.ReferenceResourceTypeId, y.ReferenceResourceTypeId))
                {
                    return false;
                }

                if (!EqualityComparer<int?>.Default.Equals(x.ReferenceResourceVersion, y.ReferenceResourceVersion))
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkReferenceSearchParamTableTypeV1Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.BaseUri?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.ReferenceResourceId?.GetHashCode(StringComparison.Ordinal) ?? 0;
                hashCode ^= obj.ReferenceResourceTypeId?.GetHashCode() ?? 0;
                hashCode ^= obj.ReferenceResourceVersion?.GetHashCode() ?? 0;

                return hashCode.GetHashCode();
            }
        }
    }
}
