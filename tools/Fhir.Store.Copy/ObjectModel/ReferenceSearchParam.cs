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
    public class ReferenceSearchParam
    {
        public ReferenceSearchParam(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            SearchParamId = reader.GetInt16(2);
            BaseUri = reader.IsDBNull(3) ? null : reader.GetString(3);
            ReferenceResourceTypeId = reader.GetInt16(4);
            ReferenceResourceId = reader.GetString(5);
            ReferenceResourceVersion = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            IsHistory = reader.GetBoolean(7);
        }

        public short ResourceTypeId { get; }
        public long ResourceSurrogateId { get; set; }
        public short SearchParamId { get; }
        public string BaseUri { get; }
        public short ReferenceResourceTypeId { get; }
        public string ReferenceResourceId { get; }
        public int? ReferenceResourceVersion { get; }
        public bool IsHistory { get; }
    }

    public static class SqlParamaterReferenceSearchParamExtension
    {
        static SqlParamaterReferenceSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
                new SqlMetaData("BaseUri", SqlDbType.VarChar, 128),
                new SqlMetaData("ReferenceResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ReferenceResourceId", SqlDbType.VarChar, 64),
                new SqlMetaData("ReferenceResourceVersion", SqlDbType.Int),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddReferenceSearchParamList(this SqlParameter param, IEnumerable<ReferenceSearchParam> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.ReferenceSearchParamList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<ReferenceSearchParam> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetSqlInt16(2, row.SearchParamId);
                if (row.BaseUri != null)
                {
                    record.SetSqlString(3, row.BaseUri);
                }
                else
                {
                    record.SetDBNull(3);
                }

                record.SetSqlInt16(4, row.ReferenceResourceTypeId);
                record.SetSqlString(5, row.ReferenceResourceId);
                if (row.ReferenceResourceVersion.HasValue)
                {
                    record.SetSqlInt32(6, row.ReferenceResourceVersion.Value);
                }
                else
                {
                    record.SetDBNull(6);
                }

                record.SetBoolean(7, row.IsHistory);
                yield return record;
            }
        }
    }
}
