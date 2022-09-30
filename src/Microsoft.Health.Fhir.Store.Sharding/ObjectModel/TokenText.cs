// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class TokenText : PrimaryKey
    {
        public TokenText(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            SearchParamId = (short)input["SearchParamId"];
            Text = (string)input["Text"];
            IsHistory = false;
        }

        public TokenText(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                Text = reader.GetString(5);
                IsHistory = reader.GetBoolean(6);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                Text = reader.GetString(3);
                IsHistory = reader.GetBoolean(4);
            }
        }

        public short SearchParamId { get; }

        public string Text { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterTokenTextExtension
    {
        static SqlParamaterTokenTextExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
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
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                record.SetSqlString(5, row.Text);
                record.SetBoolean(6, row.IsHistory);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusTokenTextExtension
    {
        public static int BulkLoadTable(this Npgsql.NpgsqlConnection connection, IEnumerable<TokenText> rows, string tableName)
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
                    writer.Write(row.Text, NpgsqlTypes.NpgsqlDbType.Varchar);
                    writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
                    c++;
                }

                writer.Complete();
            }

            return c;
        }
    }
}
