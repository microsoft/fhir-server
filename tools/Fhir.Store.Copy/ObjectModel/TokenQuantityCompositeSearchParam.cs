// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public class TokenQuantityCompositeSearchParam
    {
        public TokenQuantityCompositeSearchParam(SqlDataReader reader)
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

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

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

    public static class SqlParamaterTokenQuantityCompositeSearchParamExtension
    {
        static SqlParamaterTokenQuantityCompositeSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
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
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                if (row.SystemId1.HasValue)
                {
                    record.SetSqlInt32(3, row.SystemId1.Value);
                }
                else
                {
                    record.SetDBNull(3);
                }

                if (row.Code1 != null)
                {
                    record.SetString(4, row.Code1);
                }
                else
                {
                    record.SetDBNull(3);
                }

                if (row.SystemId2.HasValue)
                {
                    record.SetSqlInt32(5, row.SystemId2.Value);
                }
                else
                {
                    record.SetDBNull(5);
                }

                if (row.QuantityCodeId2.HasValue)
                {
                    record.SetSqlInt32(6, row.QuantityCodeId2.Value);
                }
                else
                {
                    record.SetDBNull(6);
                }

                if (row.SingleValue2.HasValue)
                {
                    record.SetDecimal(7, row.SingleValue2.Value);
                }
                else
                {
                    record.SetDBNull(7);
                }

                if (row.LowValue2.HasValue)
                {
                    record.SetDecimal(8, row.LowValue2.Value);
                }
                else
                {
                    record.SetDBNull(8);
                }

                if (row.HighValue2.HasValue)
                {
                    record.SetDecimal(9, row.HighValue2.Value);
                }
                else
                {
                    record.SetDBNull(9);
                }

                record.SetBoolean(10, row.IsHistory);
                yield return record;
            }
        }
    }
}
