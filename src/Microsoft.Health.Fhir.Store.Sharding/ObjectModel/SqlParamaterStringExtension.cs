// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class SqlParamaterStringExtension
    {
        static SqlParamaterStringExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("String", SqlDbType.VarChar, 255),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddCopyStringList(this SqlParameter param, IEnumerable<string> strings)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.StringList";
            param.Value = GetSqlDataRecords(strings);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<string> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlString(0, row);
                yield return record;
            }
        }
    }
}
