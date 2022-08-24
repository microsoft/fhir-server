﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public class ReferenceSearchParam
    {
        public ReferenceSearchParam(TransactionId transactionId, ShardletId shardletId, short sequence, DataRow input)
        {
            ResourceTypeId = (short)input["ResourceTypeId"];
            TransactionId = transactionId;
            ShardletId = shardletId;
            Sequence = sequence;
            SearchParamId = (short)input["SearchParamId"];
            var baseUri = input["BaseUri"];
            if (baseUri != DBNull.Value)
            {
                BaseUri = (string)baseUri;
            }

            ReferenceResourceTypeId = (short)input["ReferenceResourceTypeId"];
            ReferenceResourceId = (string)input["ReferenceResourceId"];
            var referenceResourceVersion = input["ReferenceResourceVersion"];
            if (referenceResourceVersion != DBNull.Value)
            {
                ReferenceResourceVersion = (int)referenceResourceVersion;
            }

            IsHistory = false;
        }

        public ReferenceSearchParam(SqlDataReader reader, bool isSharded)
        {
            if (isSharded)
            {
                throw new NotImplementedException();
            }
            else
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
        }

        public short ResourceTypeId { get; }

        public long ResourceSurrogateId { get; set; } // not sharded schema

        public TransactionId TransactionId { get; set; } // sharded schema

        public ShardletId ShardletId { get; set; } // sharded schema

        public short Sequence { get; set; } // sharded schema

        public short SearchParamId { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Correct")]
        public string BaseUri { get; }

        public short ReferenceResourceTypeId { get; }

        public string ReferenceResourceId { get; }

        public int? ReferenceResourceVersion { get; }

        public bool IsHistory { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "Readability")]
    public static class SqlParamaterReferenceSearchParamExtension
    {
        static SqlParamaterReferenceSearchParamExtension()
        {
            MetaData = new SqlMetaData[]
            {
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("TransactionId", SqlDbType.BigInt),
                new SqlMetaData("ShardletId", SqlDbType.TinyInt),
                new SqlMetaData("Sequence", SqlDbType.SmallInt),
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
                record.SetSqlInt64(1, row.TransactionId.Id);
                record.SetSqlByte(2, row.ShardletId.Id);
                record.SetSqlInt16(3, row.Sequence);
                record.SetSqlInt16(4, row.SearchParamId);
                if (row.BaseUri != null)
                {
                    record.SetSqlString(5, row.BaseUri);
                }
                else
                {
                    record.SetDBNull(5);
                }

                record.SetSqlInt16(6, row.ReferenceResourceTypeId);
                record.SetSqlString(7, row.ReferenceResourceId);
                if (row.ReferenceResourceVersion.HasValue)
                {
                    record.SetSqlInt32(8, row.ReferenceResourceVersion.Value);
                }
                else
                {
                    record.SetDBNull(8);
                }

                record.SetBoolean(9, row.IsHistory);
                yield return record;
            }
        }
    }
}
