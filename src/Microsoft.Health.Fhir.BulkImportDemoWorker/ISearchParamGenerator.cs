// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public interface ISearchParamGenerator
    {
        public string TableName { get; }

        public DataTable CreateDataTable();

        public DataRow GenerateDataRow(DataTable table, BulkCopySearchParamWrapper searchParam);
    }
}
