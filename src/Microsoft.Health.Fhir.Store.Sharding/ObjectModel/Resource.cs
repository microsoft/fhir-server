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

using Polly;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class Resource : PrimaryKey
    {
        public Resource()
        {
        }

        public Resource(SqlDataReader reader, bool isSharded, string suffix = null)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceId = suffix == null ? reader.GetString(1) : $"{reader.GetString(1)}{suffix}";
                Version = reader.GetInt32(2);
                IsHistory = reader.GetBoolean(3);
                TransactionId = new TransactionId(reader.GetInt64(4));
                ShardletId = new ShardletId(reader.GetByte(5));
                Sequence = reader.GetInt16(6);
                IsDeleted = reader.GetBoolean(7);
                RequestMethod = reader.GetString(8);
                SearchParamHash = reader.GetString(9);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceId = suffix == null ? reader.GetString(1) : $"{reader.GetString(1)}{suffix}";
                Version = reader.GetInt32(2);
                IsHistory = reader.GetBoolean(3);
                ResourceSurrogateId = reader.GetInt64(4);
                IsDeleted = reader.GetBoolean(5);
                RequestMethod = reader.GetString(6);
                RawResource = reader.GetSqlBytes(7).Value;
                IsRawResourceMetaSet = reader.GetBoolean(8);
                SearchParamHash = reader.GetString(9);
            }
        }

        public string ResourceId { get; set; }

        public int Version { get; set; }

        public bool IsHistory { get; set; }

        public bool IsDeleted { get; set; }

        public string RequestMethod { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Correct")]
        public byte[] RawResource { get; set; }

        public bool IsRawResourceMetaSet { get; set; }

        public string SearchParamHash { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
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
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("IsDeleted", SqlDbType.Bit),
                new SqlMetaData("RequestMethod", SqlDbType.VarChar, 10),
                new SqlMetaData("SearchParamHash", SqlDbType.VarChar, 64),
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
                record.SetSqlInt64(4, row.TransactionId.Id);
                record.SetSqlByte(5, row.ShardletId.Id);
                record.SetSqlInt16(6, row.Sequence);
                record.SetBoolean(7, row.IsDeleted);
                record.SetSqlString(8, row.RequestMethod);
                record.SetSqlString(9, row.SearchParamHash);
                yield return record;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class CitusResourceExtension
    {
        public static void WriteRow(NpgsqlBinaryImporter writer, Resource row)
        {
            writer.Write(row.ResourceTypeId, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.ResourceId, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(row.Version, NpgsqlTypes.NpgsqlDbType.Integer);
            writer.Write(row.IsHistory, NpgsqlTypes.NpgsqlDbType.Boolean);
            writer.Write(row.TransactionId.Id, NpgsqlTypes.NpgsqlDbType.Bigint);
            writer.Write(row.ShardletId.Id, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.Sequence, NpgsqlTypes.NpgsqlDbType.Smallint);
            writer.Write(row.IsDeleted, NpgsqlTypes.NpgsqlDbType.Boolean);
            writer.Write(row.RequestMethod, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(row.SearchParamHash, NpgsqlTypes.NpgsqlDbType.Varchar);
        }
    }
}
