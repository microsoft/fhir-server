// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import.DataGenerator
{
    internal abstract class TableBulkCopyDataGenerator
    {
        internal abstract string TableName { get; }

        public DataTable GenerateDataTable()
        {
            DataTable table = new DataTable(TableName);
            FillSchema(table);

            return table;
        }

        internal abstract void FillDataTable(DataTable table, SqlBulkCopyDataWrapper input);

        internal abstract void FillSchema(DataTable table);

        internal static void FillColumn(DataRow row, string name, object value)
        {
            row[name] = value == null ? DBNull.Value : value;
        }
    }
}
