// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public class DateTimeSearchParam
    {
        public DateTimeSearchParam(SqlDataReader reader)
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

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public DateTime StartDateTime { get; }

        public DateTime EndDateTime { get; }

        public bool IsLongerThanADay { get; }

        public bool IsHistory { get; }

        public bool IsMin { get; }

        public bool IsMax { get; }
    }

    public static class SqlParamaterDateTimeSearchParamExtension
    {
        static SqlParamaterDateTimeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
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
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                record.SetDateTime(3, row.StartDateTime);
                record.SetDateTime(4, row.EndDateTime);
                record.SetBoolean(5, row.IsLongerThanADay);
                record.SetBoolean(6, row.IsHistory);
                record.SetBoolean(7, row.IsMin);
                record.SetBoolean(8, row.IsMax);
                yield return record;
            }
        }
    }
}
