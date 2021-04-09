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
    internal class TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator : SearchParamtersTableBulkCopyDataGenerator
    {
        private ITableValuedParameterRowGenerator<ResourceMetadata, TokenTokenCompositeSearchParamTableTypeV1Row> _searchParamGenerator;

        public TokenTokenCompositeSearchParamsTableBulkCopyDataGenerator(ITableValuedParameterRowGenerator<ResourceMetadata, TokenTokenCompositeSearchParamTableTypeV1Row> searchParamGenerator)
        {
            _searchParamGenerator = searchParamGenerator;
        }

        internal override string TableName
        {
            get
            {
                return VLatest.TokenTokenCompositeSearchParam.TableName;
            }
        }

        internal override void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input)
        {
            IEnumerable<TokenTokenCompositeSearchParamTableTypeV1Row> searchParams = _searchParamGenerator.GenerateRows(input.Metadata);

            foreach (TokenTokenCompositeSearchParamTableTypeV1Row searchParam in searchParams)
            {
                DataRow newRow = CreateNewRowWithCommonProperties(table, input.ResourceTypeId, input.ResourceSurrogateId, searchParam.SearchParamId);
                newRow[VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.Name] = searchParam.SystemId1;
                newRow[VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.Name] = searchParam.Code1;
                newRow[VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.Name] = searchParam.SystemId2;
                newRow[VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.Name] = searchParam.Code2;

                table.Rows.Add(newRow);
            }
        }

        internal override void FillSearchParamsSchema(DataTable table)
        {
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.SystemId1.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.Code1.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.SystemId2.Metadata.Type));
            table.Columns.Add(new DataColumn(VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.Name, VLatest.TokenTokenCompositeSearchParam.Code2.Metadata.Type));
        }
    }
}
