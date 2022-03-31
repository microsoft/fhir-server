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
    public class TokenStringCompositeSearchParam
    {
        public TokenStringCompositeSearchParam(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            SearchParamId = reader.GetInt16(2);
            SystemId1 = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            Code1 = reader.GetString(4);
            Text2 = reader.GetString(5);
            TextOverflow2 = reader.IsDBNull(6) ? null : reader.GetString(6);
            IsHistory = reader.GetBoolean(7);
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public int? SystemId1 { get; }

        public string Code1 { get; }

        public string Text2 { get; }

        public string TextOverflow2 { get; }

        public bool IsHistory { get; }
    }

    public static class SqlParamaterTokenStringCompositeSearchParamExtension
    {
        static SqlParamaterTokenStringCompositeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("SystemId1", SqlDbType.Int), // null
                new SqlMetaData("Code1", SqlDbType.VarChar, 128),
                new SqlMetaData("Text2", SqlDbType.VarChar, 256),
                new SqlMetaData("TextOverflow2", SqlDbType.VarChar, -1), // null
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenStringCompositeSearchParamList(this SqlParameter param, IEnumerable<TokenStringCompositeSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenStringCompositeSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenStringCompositeSearchParam> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                if (row.SystemId1.HasValue)
                {
                    record.SetSqlInt32(3, row.SystemId1.Value);
                }
                else
                {
                    record.SetDBNull(3);
                }

                record.SetSqlString(4, row.Code1);
                record.SetSqlString(5, row.Text2);
                if (row.TextOverflow2 != null)
                {
                    record.SetSqlString(6, row.TextOverflow2);
                }
                else
                {
                    record.SetDBNull(6);
                }

                record.SetBoolean(7, row.IsHistory);
                yield return record;
            }
        }
    }
}
