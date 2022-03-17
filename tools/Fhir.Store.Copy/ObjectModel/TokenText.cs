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
    public class TokenText
    {
        public TokenText(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            SearchParamId = reader.GetInt16(2);
            Text = reader.GetString(3);
            IsHistory = reader.GetBoolean(4);
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public string Text { get; }

        public bool IsHistory { get; }
    }

    public static class SqlParamaterTokenTextExtension
    {
        static SqlParamaterTokenTextExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("Text", SqlDbType.VarChar, 400),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenTextList(this SqlParameter param, IEnumerable<TokenText> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenTextList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenText> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                record.SetSqlString(3, row.Text);
                record.SetBoolean(4, row.IsHistory);
                yield return record;
            }
        }
    }
}
