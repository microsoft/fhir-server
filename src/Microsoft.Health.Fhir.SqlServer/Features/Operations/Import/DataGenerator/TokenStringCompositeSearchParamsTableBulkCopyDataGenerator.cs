// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal class TokenStringCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, TokenStringCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        public TokenStringCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, TokenStringCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.TokenStringCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<TokenStringCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (TokenStringCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.Name, searchParam.SystemId1);
                FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.Code1.Metadata.Name, searchParam.Code1);
                FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.Text2.Metadata.Name, searchParam.Text2);
                FillColumn(newRow, VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.Name, searchParam.TextOverflow2);

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenStringCompositeSearchParam.SystemId1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenStringCompositeSearchParam.Code1.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.Text2.Metadata.Name, VLatest.TokenStringCompositeSearchParam.Text2.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.Name, VLatest.TokenStringCompositeSearchParam.TextOverflow2.Metadata.SqlDbType.GetGeneralType()));
        }
    }
}
