// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal abstract class TableBulkCopyDataGenerator<TDataType>
    {
        internal abstract string TableName { get; }

        internal DataTable GenerateDataTable()
        {
            DataTable table = new DataTable(TableName);
            FillSchema(table);

            return table;
        }

        internal abstract void FillDataTable(DataTable table, TDataType input);

        internal abstract void FillSchema(DataTable table);
    }
}
