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
    public class TokenSearchParam
    {
        public TokenSearchParam(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            SearchParamId = reader.GetInt16(2);
            SystemId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            Code = reader.GetString(4);
            IsHistory = reader.GetBoolean(5);
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; }

        public short SearchParamId { get; }

        public int? SystemId { get; }

        public string Code { get; }

        public bool IsHistory { get; }
    }

    public static class SqlParamaterTokenSearchParamExtension
    {
        static SqlParamaterTokenSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("SystemId", SqlDbType.Int),
                new SqlMetaData("Code", SqlDbType.VarChar, 128),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTokenSearchParamList(this SqlParameter param, IEnumerable<TokenSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TokenSearchParam> rows)
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

                record.SetSqlString(4, row.Code);
                record.SetBoolean(5, row.IsHistory);
                yield return record;
            }
        }
    }
}
