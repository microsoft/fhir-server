// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class ResourceKey
    {
        public ResourceKey()
        {
        }

        public ResourceKey(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceId = reader.GetString(1);
            TransactionId = new TransactionId(reader.GetInt64(2));
            ShardletId = new ShardletId(reader.GetByte(3));
            Sequence = reader.GetInt16(4);
        }

        public short ResourceTypeId { get; set; }

        public string ResourceId { get; set; }

        public TransactionId TransactionId { get; set; }

        public ShardletId ShardletId { get; set; }

        public short Sequence { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterResourceKeyExtension
    {
        static SqlParamaterResourceKeyExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceId", SqlDbType.VarChar, 64),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddResourceKeyList(this SqlParameter param, IEnumerable<ResourceKey> resources)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.ResourceKeyList";
            param.Value = GetSqlDataRecords(resources);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<ResourceKey> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlString(1, row.ResourceId);
                record.SetSqlInt64(2, row.TransactionId.Id);
                record.SetSqlByte(3, row.ShardletId.Id);
                record.SetSqlInt16(4, row.Sequence);
                yield return record;
            }
        }
    }
}
