// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

using Npgsql;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class TokenTokenCompositeSearchParam : PrimaryKey
    {
        public TokenTokenCompositeSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            SearchParamId = (short)input["SearchParamId"];
            var systemId1 = input["SystemId1"];
            if (systemId1 != DBNull.Value)
            {
                SystemId1 = (int)systemId1;
            }

            Code1 = (string)input["Code1"];
            var systemId2 = input["SystemId2"];
            if (systemId2 != DBNull.Value)
            {
                SystemId2 = (int)systemId2;
            }

            CodeId2 = (string)input["Code2"];
            IsHistory = false;
        }

        public TokenTokenCompositeSearchParam(SqlDataReader reader, bool isSharded, IDictionary<(ShardletId shardletId, short sequence), string> resourceIdMap = null)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                SystemId1 = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                Code1 = reader.GetString(6);
                SystemId2 = reader.IsDBNull(7) ? null : reader.GetInt32(7);
                CodeId2 = reader.GetString(8);
                IsHistory = reader.GetBoolean(9);
                if (resourceIdMap != null)
                {
                    ResourceId = resourceIdMap[(ShardletId, Sequence)];
                }
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                SystemId1 = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                Code1 = reader.GetString(4);
                SystemId2 = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                CodeId2 = reader.GetString(6);
                IsHistory = reader.GetBoolean(7);
            }
        }

        public short SearchParamId { get; }

        public int? SystemId1 { get; }

        public string Code1 { get; }

        public int? SystemId2 { get; }

        public string CodeId2 { get; }

        public bool IsHistory { get; }

        public string ResourceId { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterTokenTokenCompositeSearchParamExtension
    {
        static SqlParamaterTokenTokenCompositeSearchParamExtension()
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
                new SqlMetaData("SystemId2", SqlDbType.Int), // null
                new SqlMetaData("CodeId2", SqlDbType.VarChar, 128),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenTokenCompositeSearchParamList(this SqlParameter param, IEnumerable<TokenTokenCompositeSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenTokenCompositeSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenTokenCompositeSearchParam> rows)
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

                record.SetString(6, row.Code1);
                if (row.SystemId2.HasValue)
                {
                    record.SetSqlInt32(7, row.SystemId2.Value);
                }
                else
                {
                    record.SetDBNull(7);
                }

                record.SetString(8, row.CodeId2);
                record.SetBoolean(9, row.IsHistory);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusTokenTokenCompositeSearchParamExtension
    {
        public static void WriteRow(NpgsqlBinaryImporter writer, TokenTokenCompositeSearchParam row)
        {
            writer.Write(row.ResourceTypeId, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.TransactionId.Id, NpgsqlTypes.NpgsqlDbType.Bigint);
            writer.Write(row.ShardletId.Id, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.Sequence, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.SearchParamId, NpgsqlTypes.NpgsqlDbType.Smallint);
            if (row.SystemId1.HasValue)
            {
                writer.Write(row.SystemId1.Value, NpgsqlTypes.NpgsqlDbType.Integer);
            }
            else
            {
                writer.WriteNull();
            }

            writer.Write(row.Code1, NpgsqlTypes.NpgsqlDbType.Varchar);
            if (row.SystemId2.HasValue)
            {
                writer.Write(row.SystemId2.Value, NpgsqlTypes.NpgsqlDbType.Integer);
            }
            else
            {
                writer.WriteNull();
            }

            writer.Write(row.CodeId2, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
        }
    }
}
