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
    public class TokenQuantityCompositeSearchParam : PrimaryKey
    {
        public TokenQuantityCompositeSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
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

            var code1 = input["Code1"];
            if (code1 != DBNull.Value)
            {
                Code1 = (string)code1;
            }

            var systemId2 = input["SystemId2"];
            if (systemId2 != DBNull.Value)
            {
                SystemId2 = (int)systemId2;
            }

            var quantityCodeId2 = input["QuantityCodeId2"];
            if (quantityCodeId2 != DBNull.Value)
            {
                QuantityCodeId2 = (int)quantityCodeId2;
            }

            var singleValue2 = input["SingleValue2"];
            if (singleValue2 != DBNull.Value)
            {
                SingleValue2 = (decimal)singleValue2;
            }

            var lowValue2 = input["LowValue2"];
            if (lowValue2 != DBNull.Value)
            {
                LowValue2 = (decimal)lowValue2;
            }

            var highValue2 = input["HighValue2"];
            if (highValue2 != DBNull.Value)
            {
                HighValue2 = (decimal)highValue2;
            }

            IsHistory = false;
        }

        public TokenQuantityCompositeSearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                SystemId1 = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                Code1 = reader.IsDBNull(6) ? null : reader.GetString(6);
                SystemId2 = reader.IsDBNull(7) ? null : reader.GetInt32(7);
                QuantityCodeId2 = reader.IsDBNull(8) ? null : reader.GetInt32(8);
                SingleValue2 = reader.IsDBNull(9) ? null : reader.GetDecimal(9);
                LowValue2 = reader.IsDBNull(10) ? null : reader.GetDecimal(10);
                HighValue2 = reader.IsDBNull(11) ? null : reader.GetDecimal(11);
                IsHistory = reader.GetBoolean(12);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                SystemId1 = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                Code1 = reader.IsDBNull(4) ? null : reader.GetString(4);
                SystemId2 = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                QuantityCodeId2 = reader.IsDBNull(6) ? null : reader.GetInt32(6);
                SingleValue2 = reader.IsDBNull(7) ? null : reader.GetDecimal(7);
                LowValue2 = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
                HighValue2 = reader.IsDBNull(9) ? null : reader.GetDecimal(9);
                IsHistory = reader.GetBoolean(10);
            }
        }

        public short SearchParamId { get; }

        public int? SystemId1 { get; }

        public string Code1 { get; }

        public int? SystemId2 { get; }

        public int? QuantityCodeId2 { get; }

        public decimal? SingleValue2 { get; }

        public decimal? LowValue2 { get; }

        public decimal? HighValue2 { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterTokenQuantityCompositeSearchParamExtension
    {
        static SqlParamaterTokenQuantityCompositeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("SystemId1", SqlDbType.Int),
                new SqlMetaData("Code1", SqlDbType.VarChar, 128),
                new SqlMetaData("SystemId2", SqlDbType.Int),
                new SqlMetaData("QuantityCodeId2", SqlDbType.Int),
                new SqlMetaData("SingleValue2", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("LowValue2", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("HighValue2", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenQuantityCompositeSearchParamList(this SqlParameter param, IEnumerable<TokenQuantityCompositeSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenQuantityCompositeSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenQuantityCompositeSearchParam> rows)
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

                if (row.Code1 != null)
                {
                    record.SetString(6, row.Code1);
                }
                else
                {
                    record.SetDBNull(6);
                }

                if (row.SystemId2.HasValue)
                {
                    record.SetSqlInt32(7, row.SystemId2.Value);
                }
                else
                {
                    record.SetDBNull(7);
                }

                if (row.QuantityCodeId2.HasValue)
                {
                    record.SetSqlInt32(8, row.QuantityCodeId2.Value);
                }
                else
                {
                    record.SetDBNull(8);
                }

                if (row.SingleValue2.HasValue)
                {
                    record.SetDecimal(9, row.SingleValue2.Value);
                }
                else
                {
                    record.SetDBNull(9);
                }

                if (row.LowValue2.HasValue)
                {
                    record.SetDecimal(10, row.LowValue2.Value);
                }
                else
                {
                    record.SetDBNull(10);
                }

                if (row.HighValue2.HasValue)
                {
                    record.SetDecimal(11, row.HighValue2.Value);
                }
                else
                {
                    record.SetDBNull(11);
                }

                record.SetBoolean(12, row.IsHistory);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusTokenQuantityCompositeSearchParamExtension
    {
        public static int BulkLoadTable(this Npgsql.NpgsqlConnection connection, IEnumerable<TokenQuantityCompositeSearchParam> rows, string tableName)
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
                    if (row.SystemId1.HasValue)
                    {
                        writer.Write(row.SystemId1.Value, NpgsqlTypes.NpgsqlDbType.Integer);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.Code1 != null)
                    {
                        writer.Write(row.Code1, NpgsqlTypes.NpgsqlDbType.Varchar);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.SystemId2.HasValue)
                    {
                        writer.Write(row.SystemId2.Value, NpgsqlTypes.NpgsqlDbType.Integer);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.QuantityCodeId2.HasValue)
                    {
                        writer.Write(row.QuantityCodeId2.Value, NpgsqlTypes.NpgsqlDbType.Integer);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.SingleValue2.HasValue)
                    {
                        writer.Write(row.SingleValue2.Value, NpgsqlTypes.NpgsqlDbType.Numeric);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.LowValue2.HasValue)
                    {
                        writer.Write(row.LowValue2.Value, NpgsqlTypes.NpgsqlDbType.Numeric);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    if (row.HighValue2.HasValue)
                    {
                        writer.Write(row.HighValue2.Value, NpgsqlTypes.NpgsqlDbType.Numeric);
                    }
                    else
                    {
                        writer.WriteNull();
                    }

                    writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
                    c++;
                }

                writer.Complete();
            }

            return c;
        }
    }
}
