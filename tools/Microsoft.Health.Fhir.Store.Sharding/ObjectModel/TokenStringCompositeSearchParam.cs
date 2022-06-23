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
    public class TokenStringCompositeSearchParam
    {
        public TokenStringCompositeSearchParam(SqlDataReader reader, bool isSharded)
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
                SystemId1 = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                Code1 = reader.GetString(4);
                Text2 = reader.GetString(5);
                TextOverflow2 = reader.IsDBNull(6) ? null : reader.GetString(6);
                IsHistory = reader.GetBoolean(7);
            }
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; } // not sharded schema

        public TransactionId TransactionId { get; set; } // sharded schema

        public ShardletId ShardletId { get; set; } // sharded schema

        public short Sequence { get; set; } // sharded schema

        public short SearchParamId { get; }

        public int? SystemId1 { get; }

        public string Code1 { get; }

        public string Text2 { get; }

        public string TextOverflow2 { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterTokenStringCompositeSearchParamExtension
    {
        static SqlParamaterTokenStringCompositeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
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
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                if (row.SystemId1.HasValue)
                {
                    record.SetSqlInt32(5, row.SystemId1.Value);
                }
                else
                {
                    record.SetDBNull(5);
                }

                record.SetSqlString(6, row.Code1);
                record.SetSqlString(7, row.Text2);
                if (row.TextOverflow2 != null)
                {
                    record.SetSqlString(8, row.TextOverflow2);
                }
                else
                {
                    record.SetDBNull(8);
                }

                record.SetBoolean(9, row.IsHistory);
                yield return record;
            }
        }
    }
}
