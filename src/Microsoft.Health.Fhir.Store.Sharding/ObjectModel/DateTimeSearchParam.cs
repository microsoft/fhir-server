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
    public class DateTimeSearchParam : PrimaryKey
    {
        public DateTimeSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            SearchParamId = (short)input["SearchParamId"];
            StartDateTime = (DateTime)input["StartDateTime"];
            EndDateTime = (DateTime)input["EndDateTime"];
            IsLongerThanADay = (bool)input["IsLongerThanADay"];
            IsHistory = false;
            IsMin = (bool)input["IsMin"];
            IsMax = (bool)input["IsMax"];
        }

        public DateTimeSearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                SearchParamId = reader.GetInt16(4);
                StartDateTime = reader.GetDateTime(5);
                EndDateTime = reader.GetDateTime(6);
                IsLongerThanADay = reader.GetBoolean(7);
                IsHistory = reader.GetBoolean(8);
                IsMin = reader.GetBoolean(9);
                IsMax = reader.GetBoolean(10);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                SearchParamId = reader.GetInt16(2);
                StartDateTime = reader.GetDateTime(3);
                EndDateTime = reader.GetDateTime(4);
                IsLongerThanADay = reader.GetBoolean(5);
                IsHistory = reader.GetBoolean(6);
                IsMin = reader.GetBoolean(7);
                IsMax = reader.GetBoolean(8);
            }
        }

        public short SearchParamId { get; }

        public DateTime StartDateTime { get; }

        public DateTime EndDateTime { get; }

        public bool IsLongerThanADay { get; }

        public bool IsHistory { get; }

        public bool IsMin { get; }

        public bool IsMax { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterDateTimeSearchParamExtension
    {
        static SqlParamaterDateTimeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("StartDateTime", SqlDbType.DateTime2),
                new SqlMetaData("EndDateTime", SqlDbType.DateTime2),
                new SqlMetaData("IsLongerThanADay", SqlDbType.Bit),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
                new SqlMetaData("IsMin", SqlDbType.Bit),
                new SqlMetaData("IsMax", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddDateTimeSearchParamList(this SqlParameter param, IEnumerable<DateTimeSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.DateTimeSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<DateTimeSearchParam> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                record.SetDateTime(5, row.StartDateTime);
                record.SetDateTime(6, row.EndDateTime);
                record.SetBoolean(7, row.IsLongerThanADay);
                record.SetBoolean(8, row.IsHistory);
                record.SetBoolean(9, row.IsMin);
                record.SetBoolean(10, row.IsMax);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusDateTimeSearchParamExtension
    {
        public static void WriteRow(NpgsqlBinaryImporter writer, DateTimeSearchParam row)
        {
            writer.Write(row.ResourceTypeId, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.TransactionId.Id, NpgsqlTypes.NpgsqlDbType.Bigint);
            writer.Write(row.ShardletId.Id, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.Sequence, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.SearchParamId, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.StartDateTime, NpgsqlTypes.NpgsqlDbType.Timestamp);
            writer.Write(row.EndDateTime, NpgsqlTypes.NpgsqlDbType.Timestamp);
            writer.Write(row.IsLongerThanADay, NpgsqlTypes.NpgsqlDbType.Boolean);
            writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
            writer.Write(row.IsMin, NpgsqlTypes.NpgsqlDbType.Boolean);
            writer.Write(row.IsMax, NpgsqlTypes.NpgsqlDbType.Boolean);
        }
    }
}
