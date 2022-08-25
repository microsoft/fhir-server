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
    public class Resource
    {
        public Resource(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceId = reader.GetString(1);
            Version = reader.GetInt32(2);
            IsHistory = reader.GetBoolean(3);
            ResourceSurrogateId = reader.GetInt64(4);
            IsDeleted = reader.GetBoolean(5);
            RequestMethod = reader.GetString(6);
            RawResource = reader.GetSqlBytes(7).Value;
            IsRawResourceMetaSet = reader.GetBoolean(8);
            SearchParamHash = reader.GetString(9);
        }

        public short ResourceTypeId { get; }
        public string ResourceId { get; }
        public int Version { get; }
        public bool IsHistory { get; }
        public long ResourceSurrogateId { get; set; }
        public bool IsDeleted { get; }
        public string RequestMethod { get; }
        public byte[] RawResource { get; }
        public bool IsRawResourceMetaSet { get; }
        public string SearchParamHash { get; }
    }

    public static class SqlParamaterResourceExtension
    {
        static SqlParamaterResourceExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceId", SqlDbType.VarChar, 64),
                new SqlMetaData("Version", SqlDbType.Int),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("IsDeleted", SqlDbType.Bit),
                new SqlMetaData("RequestMethod", SqlDbType.VarChar, 10),
                new SqlMetaData("RawResource", SqlDbType.VarBinary, -1),
                new SqlMetaData("IsRawResourceMetaSet", SqlDbType.Bit),
                new SqlMetaData("SearchParamHash", SqlDbType.VarChar, 64)
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddResourceList(this SqlParameter param, IEnumerable<Resource> resources)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.ResourceList";
            param.Value = GetSqlDataRecords(resources);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<Resource> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlString(1, row.ResourceId);
                record.SetSqlInt32(2, row.Version);
                record.SetBoolean(3, row.IsHistory);
                record.SetSqlInt64(4, row.ResourceSurrogateId);
                record.SetBoolean(5, row.IsDeleted);
                record.SetSqlString(6, row.RequestMethod);
                record.SetSqlBinary(7, row.RawResource);
                record.SetBoolean(8, row.IsRawResourceMetaSet);
                record.SetSqlString(9, row.SearchParamHash);
                yield return record;
            }
        }
    }
}
