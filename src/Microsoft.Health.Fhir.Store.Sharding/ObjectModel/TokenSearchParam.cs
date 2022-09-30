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
    public class TokenSearchParam : PrimaryKey
    {
        public TokenSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            SearchParamId = (short)input["SearchParamId"];
            var systemId = input["SystemId"];
            if (systemId != DBNull.Value)
            {
                SystemId = (int)systemId;
            }

            var code = input["Code"];
            if (code != DBNull.Value)
            {
                Code = (string)code;
            }

            IsHistory = false;
        }

        public TokenSearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                SystemId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                Code = reader.GetString(6);
                IsHistory = reader.GetBoolean(7);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                SystemId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                Code = reader.GetString(4);
                IsHistory = reader.GetBoolean(5);
            }
        }

        public short SearchParamId { get; }

        public int? SystemId { get; }

        public string Code { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterTokenSearchParamExtension
    {
        static SqlParamaterTokenSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("SystemId", SqlDbType.Int),
                new SqlMetaData("Code", SqlDbType.VarChar, 128),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenSearchParamList(this SqlParameter param, IEnumerable<TokenSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenSearchParam> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                if (row.SystemId.HasValue)
                {
                    record.SetSqlInt32(5, row.SystemId.Value);
                }
                else
                {
                    record.SetDBNull(5);
                }

                record.SetSqlString(6, row.Code);
                record.SetBoolean(7, row.IsHistory);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusTokenSearchParamExtension
    {
        public static int BulkLoadTable(this Npgsql.NpgsqlConnection connection, IEnumerable<TokenSearchParam> rows, string tableName)
        {
            int c = 0;

            using (var writer = connection.BeginBinaryImport($"COPY {tableName} FROM STDIN (FORMAT BINARY)"))
            {
                foreach (var row in rows)
                {
                    writer.StartRow();
                    writer.Write(row.ResourceTypeId, NpgsqlTypes.NpgsqlDbType.Smallint);
                    writer.Write(row.TransactionId.Id, NpgsqlTypes.NpgsqlDbType.Bigint);
                    writer.Write(row.ShardletId.Id, NpgsqlTypes.NpgsqlDbType.Smallint);
                    writer.Write(row.Sequence, NpgsqlTypes.NpgsqlDbType.Smallint);
                    writer.Write(row.SearchParamId, NpgsqlTypes.NpgsqlDbType.Smallint);
                    if (row.SystemId.HasValue)
                    {
                        writer.Write(row.SystemId.Value, NpgsqlTypes.NpgsqlDbType.Integer);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    writer.Write(row.Code, NpgsqlTypes.NpgsqlDbType.Varchar);
                    writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
                    c++;
                }

                writer.Complete();
            }

            return c;
        }
    }
}
