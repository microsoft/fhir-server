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
    public class CompartmentAssignment
    {
        public CompartmentAssignment(SqlDataReader reader)
        {
            ResourceTypeId = reader.GetInt16(0);
            ResourceSurrogateId = reader.GetInt64(1);
            CompartmentTypeId = reader.GetByte(2);
            ReferenceResourceId = reader.GetString(3);
            IsHistory = reader.GetBoolean(4);
        }

        public short ResourceTypeId { get; }
        public long ResourceSurrogateId { get; set; }
        public byte CompartmentTypeId { get; }
        public string ReferenceResourceId { get; }
        public bool IsHistory { get; }
    }

    public static class SqlParamaterCompartmentAssignmentExtension
    {
        static SqlParamaterCompartmentAssignmentExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
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
                record.SetSqlInt64(1, row.ResourceSurrogateId);
                record.SetByte(2, row.CompartmentTypeId);
                record.SetSqlString(3, row.ReferenceResourceId);
                record.SetBoolean(4, row.IsHistory);
                yield return record;
            }
        }
    }
}
