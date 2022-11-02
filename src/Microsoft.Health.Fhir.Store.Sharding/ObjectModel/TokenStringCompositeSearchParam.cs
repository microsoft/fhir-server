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
    public class TokenStringCompositeSearchParam : PrimaryKey
    {
        public TokenStringCompositeSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
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
            Text2 = (string)input["Text2"];
            var textOverflow2 = input["TextOverflow2"];
            if (textOverflow2 != DBNull.Value)
            {
                TextOverflow2 = (string)textOverflow2;
            }

            IsHistory = false;
        }

        public TokenStringCompositeSearchParam(SqlDataReader reader, bool isSharded, IDictionary<(ShardletId shardletId, short sequence), string> resourceIdMap = null)
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
                Text2 = reader.GetString(7);
                TextOverflow2 = reader.IsDBNull(8) ? null : reader.GetString(8);
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
                Text2 = reader.GetString(5);
                TextOverflow2 = reader.IsDBNull(6) ? null : reader.GetString(6);
                IsHistory = reader.GetBoolean(7);
            }
        }

        public short SearchParamId { get; }

        public int? SystemId1 { get; }

        public string Code1 { get; }

        public string Text2 { get; }

        public string TextOverflow2 { get; }

        public bool IsHistory { get; }

        public string ResourceId { get; }
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusTokenStringCompositeSearchParamExtension
    {
        public static void WriteRow(NpgsqlBinaryImporter writer, TokenStringCompositeSearchParam row)
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
            writer.Write(row.Text2, NpgsqlTypes.NpgsqlDbType.Varchar);
            if (row.TextOverflow2 != null)
            {
                writer.Write(row.TextOverflow2, NpgsqlTypes.NpgsqlDbType.Text);
            }
            else
            {
                writer.WriteNull();
            }

            writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
        }
    }
}
