// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class StringSearchParam
    {
        public StringSearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                throw new NotImplementedException();
            }
            else
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
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; } // not sharded schema

        public TransactionId TransactionId { get; set; } // sharded schema

        public ShardletId ShardletId { get; set; } // sharded schema

        public short Sequence { get; set; } // sharded schema

        public short SearchParamId { get; }

        public string Text { get; }

        public string TextOverflow { get; }

        public bool IsHistory { get; }

        public bool? IsMin { get; }

        public bool? IsMax { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterStringSearchParamExtension
    {
        static SqlParamaterStringSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
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
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                record.SetSqlString(5, row.Text);
                if (row.TextOverflow != null)
                {
                    record.SetSqlString(6, row.TextOverflow);
                }
                else
                {
                    record.SetDBNull(6);
                }

                record.SetBoolean(7, row.IsHistory);
                if (row.IsMin.HasValue)
                {
                    record.SetBoolean(8, row.IsMin.Value);
                }
                else
                {
                    record.SetDBNull(8);
                }

                if (row.IsMax.HasValue)
                {
                    record.SetBoolean(9, row.IsMax.Value);
                }
                else
                {
                    record.SetDBNull(9);
                }

                yield return record;
            }
        }
    }
}
