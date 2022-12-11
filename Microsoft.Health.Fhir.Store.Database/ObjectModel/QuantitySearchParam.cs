// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
#pragma warning disable SA1402 // File may only contain a single type

using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Database
{
    public class QuantitySearchParam
    {
        public QuantitySearchParam(SqlDataReader reader)
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

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public int? SystemId { get; }

        public int? QuantityCodeId { get; }

        public decimal? SingleValue { get; }

        public decimal LowValue { get; }

        public decimal HighValue { get; }

        public bool IsHistory { get; }
    }

    public static class SqlParamaterQuantitySearchParamExtension
    {
        static SqlParamaterQuantitySearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
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
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                if (row.SystemId.HasValue)
                {
                    record.SetSqlInt32(3, row.SystemId.Value);
                }
                else
                {
                    record.SetDBNull(3);
                }

                if (row.QuantityCodeId.HasValue)
                {
                    record.SetSqlInt32(4, row.QuantityCodeId.Value);
                }
                else
                {
                    record.SetDBNull(4);
                }

                if (row.SingleValue.HasValue)
                {
                    record.SetDecimal(5, row.SingleValue.Value);
                }
                else
                {
                    record.SetDBNull(5);
                }

                record.SetDecimal(6, row.LowValue);
                record.SetDecimal(7, row.HighValue);
                record.SetBoolean(8, row.IsHistory);
                yield return record;
            }
        }
    }
}
