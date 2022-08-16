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
    public class CompartmentAssignment
    {
        public CompartmentAssignment(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            CompartmentTypeId = (byte)input["CompartmentTypeId"];
            ReferenceResourceId = (string)input["ReferenceResourceId"];
            IsHistory = false;
        }

        public CompartmentAssignment(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                ResourceTypeId = reader.GetInt16(0);
                TransactionId = new TransactionId(reader.GetInt64(1));
                ShardletId = new ShardletId(reader.GetByte(2));
                Sequence = reader.GetInt16(3);
                CompartmentTypeId = reader.GetByte(4);
                ReferenceResourceId = reader.GetString(5);
                IsHistory = reader.GetBoolean(6);
            }
            else
            {
                ResourceTypeId = reader.GetInt16(0);
                ResourceSurrogateId = reader.GetInt64(1);
                CompartmentTypeId = reader.GetByte(2);
                ReferenceResourceId = reader.GetString(3);
                IsHistory = reader.GetBoolean(4);
            }
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; } // not sharded schema

        public TransactionId TransactionId { get; set; } // sharded schema

        public ShardletId ShardletId { get; set; } // sharded schema

        public short Sequence { get; set; } // sharded schema

        public byte CompartmentTypeId { get; }

        public string ReferenceResourceId { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterCompartmentAssignmentExtension
    {
        static SqlParamaterCompartmentAssignmentExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
                new SqlMetaData("CompartmentTypeId", SqlDbType.TinyInt),
                new SqlMetaData("ReferenceResourceId", SqlDbType.VarChar, 64),
                new SqlMetaData("IsHistory", SqlDbType.Bit),
            };
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddCompartmentAssignmentList(this SqlParameter param, IEnumerable<CompartmentAssignment> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.CompartmentAssignmentList";
            param.Value = rows == null ? null : GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<CompartmentAssignment> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt16(0, row.ResourceTypeId);
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetByte(4, row.CompartmentTypeId);
                record.SetSqlString(5, row.ReferenceResourceId);
                record.SetBoolean(6, row.IsHistory);
                yield return record;
            }
        }
    }
}
