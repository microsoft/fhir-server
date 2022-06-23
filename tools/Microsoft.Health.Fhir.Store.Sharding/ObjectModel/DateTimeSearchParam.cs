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
    public class DateTimeSearchParam
    {
        public DateTimeSearchParam(SqlDataReader reader, bool isSharded)
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
                StartDateTime = reader.GetDateTime(3);
                EndDateTime = reader.GetDateTime(4);
                IsLongerThanADay = reader.GetBoolean(5);
                IsHistory = reader.GetBoolean(6);
                IsMin = reader.GetBoolean(7);
                IsMax = reader.GetBoolean(8);
            }
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; } // not sharded schema

        public TransactionId TransactionId { get; set; } // sharded schema

        public ShardletId ShardletId { get; set; } // sharded schema

        public short Sequence { get; set; } // sharded schema

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
}
