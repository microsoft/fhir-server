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
    public class QuantitySearchParam : PrimaryKey
    {
        public QuantitySearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
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

            var quantityCodeId = input["QuantityCodeId"];
            if (quantityCodeId != DBNull.Value)
            {
                QuantityCodeId = (int)quantityCodeId;
            }

            var singleValue = input["SingleValue"];
            if (singleValue != DBNull.Value)
            {
                SingleValue = (decimal)singleValue;
            }

            LowValue = (decimal)input["LowValue"];
            HighValue = (decimal)input["HighValue"];
            IsHistory = false;
        }

        public QuantitySearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                SystemId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                QuantityCodeId = reader.IsDBNull(6) ? null : reader.GetInt32(6);
                SingleValue = reader.IsDBNull(7) ? null : reader.GetDecimal(7);
                LowValue = reader.GetDecimal(8);
                HighValue = reader.GetDecimal(9);
                IsHistory = reader.GetBoolean(10);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                SystemId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                QuantityCodeId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                SingleValue = reader.IsDBNull(5) ? null : reader.GetDecimal(5);
                LowValue = reader.GetDecimal(6);
                HighValue = reader.GetDecimal(7);
                IsHistory = reader.GetBoolean(8);
            }
        }

        public short SearchParamId { get; }

        public int? SystemId { get; }

        public int? QuantityCodeId { get; }

        public decimal? SingleValue { get; }

        public decimal LowValue { get; }

        public decimal HighValue { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterQuantitySearchParamExtension
    {
        static SqlParamaterQuantitySearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("SystemId", SqlDbType.Int),
                new SqlMetaData("QuantityCodeId", SqlDbType.Int),
                new SqlMetaData("SingleValue", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("LowValue", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("HighValue", SqlDbType.Decimal, 18, 6),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddQuantitySearchParamList(this SqlParameter param, IEnumerable<QuantitySearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.QuantitySearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<QuantitySearchParam> rows)
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

                if (row.QuantityCodeId.HasValue)
                {
                    record.SetSqlInt32(6, row.QuantityCodeId.Value);
                }
                else
                {
                    record.SetDBNull(6);
                }

                if (row.SingleValue.HasValue)
                {
                    record.SetDecimal(7, row.SingleValue.Value);
                }
                else
                {
                    record.SetDBNull(7);
                }

                record.SetDecimal(8, row.LowValue);
                record.SetDecimal(9, row.HighValue);
                record.SetBoolean(10, row.IsHistory);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusQuantitySearchParamExtension
    {
        public static void WriteRow(NpgsqlBinaryImporter writer, QuantitySearchParam row)
        {
            writer.Write(row.ResourceTypeId);
            writer.Write(row.TransactionId.Id);
            writer.Write(row.ShardletId.Id);
            writer.Write(row.Sequence);
            writer.Write(row.SearchParamId);
            if (row.SystemId.HasValue)
            {
                writer.Write(row.SystemId.Value);
            }
            else
            {
                writer.WriteNull();
            }

            if (row.QuantityCodeId.HasValue)
            {
                writer.Write(row.QuantityCodeId.Value);
            }
            else
            {
                writer.WriteNull();
            }

            if (row.SingleValue.HasValue)
            {
                writer.Write(row.SingleValue.Value);
            }
            else
            {
                writer.WriteNull();
            }

            writer.Write(row.LowValue);
            writer.Write(row.HighValue);
            writer.Write(row.IsHistory);
        }
    }
}
