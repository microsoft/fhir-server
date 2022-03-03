﻿// -------------------------------------------------------------------------------------------------
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
    internal class DateTimeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkDateTimeSearchParamTableTypeV2Row> _searchParamGenerator;

        internal DateTimeSearchParamsTableBulkCopyDataGenerator()
        {
        }

        public DateTimeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkDateTimeSearchParamTableTypeV2Row> searchParamGenerator)
        {
            EnsureArg.IsNotNull(searchParamGenerator, nameof(searchParamGenerator));

            _searchParamGenerator = searchParamGenerator;
        }

        internal static BulkDateTimeSearchParamTableTypeV2RowComparer Comparer { get; } = new BulkDateTimeSearchParamTableTypeV2RowComparer();

        internal override string TableName
        {
            get
            {
                return VLatest.DateTimeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            EnsureArg.IsNotNull(table, nameof(table));
            EnsureArg.IsNotNull(input, nameof(input));

            IEnumerable<BulkDateTimeSearchParamTableTypeV2Row> searchParams = _searchParamGenerator.GenerateRows(new ResourceWrapper[] { input.Resource });

            foreach (BulkDateTimeSearchParamTableTypeV2Row searchParam in Distinct(searchParams))
            {
                FillDataTable(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam);
            }
        }

        internal static void FillDataTable(DataTable table, short resourceTypeId, long resourceSurrogateId, BulkDateTimeSearchParamTableTypeV2Row searchParam)
        {
            DataRow newRow = CreateNewRowWithCommonProperties(table, resourceTypeId, resourceSurrogateId, searchParam.SearchParamId);
            FillColumn(newRow, VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, searchParam.StartDateTime.DateTime);
            FillColumn(newRow, VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, searchParam.EndDateTime.DateTime);
            FillColumn(newRow, VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, searchParam.IsLongerThanADay);
            FillColumn(newRow, VLatest.DateTimeSearchParam.IsMin.Metadata.Name, searchParam.IsMin);
            FillColumn(newRow, VLatest.DateTimeSearchParam.IsMax.Metadata.Name, searchParam.IsMax);
            table.Rows.Add(newRow);
        }

        internal override void FillSchema(DataTable table)
        {
            // Columns should follow same order as sql table defination.
            table.Columns.Add(new DataColumn(ResourceTypeId.Metadata.Name, ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(ResourceSurrogateId.Metadata.Name, ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(SearchParamId.Metadata.Name, SearchParamId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, VLatest.DateTimeSearchParam.StartDateTime.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name, VLatest.DateTimeSearchParam.EndDateTime.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.Name, VLatest.DateTimeSearchParam.IsLongerThanADay.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(IsHistory.Metadata.Name, IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.IsMin.Metadata.Name, VLatest.DateTimeSearchParam.IsMin.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.DateTimeSearchParam.IsMax.Metadata.Name, VLatest.DateTimeSearchParam.IsMax.Metadata.SqlDbType.GetGeneralType()));
        }

        internal static IEnumerable<BulkDateTimeSearchParamTableTypeV2Row> Distinct(IEnumerable<BulkDateTimeSearchParamTableTypeV2Row> input)
        {
            return input.Distinct(Comparer);
        }

        internal class BulkDateTimeSearchParamTableTypeV2RowComparer : IEqualityComparer<BulkDateTimeSearchParamTableTypeV2Row>
        {
            public bool Equals(BulkDateTimeSearchParamTableTypeV2Row x, BulkDateTimeSearchParamTableTypeV2Row y)
            {
                if (x.SearchParamId != y.SearchParamId)
                {
                    return false;
                }

                if (!DateTimeOffset.Equals(x.StartDateTime, y.StartDateTime))
                {
                    return false;
                }

                if (!DateTimeOffset.Equals(x.EndDateTime, y.EndDateTime))
                {
                    return false;
                }

                if (x.IsLongerThanADay != y.IsLongerThanADay)
                {
                    return false;
                }

                if (x.IsMax != y.IsMax)
                {
                    return false;
                }

                if (x.IsMin != y.IsMin)
                {
                    return false;
                }

                return true;
            }

            public int GetHashCode(BulkDateTimeSearchParamTableTypeV2Row obj)
            {
                int hashCode = obj.SearchParamId.GetHashCode();

                hashCode ^= obj.StartDateTime.GetHashCode();
                hashCode ^= obj.EndDateTime.GetHashCode();
                hashCode ^= obj.IsLongerThanADay.GetHashCode();
                hashCode ^= obj.IsMax.GetHashCode();
                hashCode ^= obj.IsMin.GetHashCode();

                return hashCode.GetHashCode();
            }
        }
    }
}
