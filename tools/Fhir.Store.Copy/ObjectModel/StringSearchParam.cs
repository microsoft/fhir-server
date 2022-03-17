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
    public class StringSearchParam
    {
        public StringSearchParam(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            SearchParamId = reader.GetInt16(2);
            Text = reader.GetString(3);
            TextOverflow = reader.IsDBNull(4) ? null : reader.GetString(4);
            IsHistory = reader.GetBoolean(5);
            IsMin = reader.IsDBNull(6) ? null : reader.GetBoolean(6);
            IsMax = reader.IsDBNull(7) ? null : reader.GetBoolean(7);
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public string Text { get; }

        public string TextOverflow { get; }

        public bool IsHistory { get; }

        public bool? IsMin { get; }

        public bool? IsMax { get; }
    }

    public static class SqlParamaterStringSearchParamExtension
    {
        static SqlParamaterStringSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("Text", SqlDbType.VarChar, 256),
                new SqlMetaData("TextOverflow", SqlDbType.VarChar, -1), // null
                new SqlMetaData("IsHistory", SqlDbType.Bit),
                new SqlMetaData("IsMin", SqlDbType.Bit),
                new SqlMetaData("IsMax", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddStringSearchParamList(this SqlParameter param, IEnumerable<StringSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.StringSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<StringSearchParam> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                record.SetSqlString(3, row.Text);
                if (row.TextOverflow != null)
                {
                    record.SetSqlString(4, row.TextOverflow);
                }
                else
                {
                    record.SetDBNull(4);
                }

                record.SetBoolean(5, row.IsHistory);
                if (row.IsMin.HasValue)
                {
                    record.SetBoolean(6, row.IsMin.Value);
                }
                else
                {
                    record.SetDBNull(6);
                }

                if (row.IsMax.HasValue)
                {
                    record.SetBoolean(7, row.IsMax.Value);
                }
                else
                {
                    record.SetDBNull(7);
                }

                yield return record;
            }
        }
    }
}
