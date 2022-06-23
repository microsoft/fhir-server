// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    /// <summary>
    /// Handles all communication between the API and SQL Server.
    /// </summary>
    public partial class SqlService
    {
        public TransactionId BeginTransaction(string definition = null, DateTime? heartbeatDate = null, int timeoutSeconds = 600)
        {
            using var connection = GetConnection((ShardId?)null);
            using var command = new SqlCommand("dbo.BeginTransaction", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            var tranId = new SqlParameter("@TransactionId", SqlDbType.BigInt);
            tranId.Direction = ParameterDirection.Output;
            command.Parameters.Add(tranId);
            command.Parameters.AddWithValue("@Definition", definition);
            command.Parameters.AddWithValue("@ReturnRecordSet", false);
            command.Parameters.AddWithValue("@TimeoutSeconds", timeoutSeconds);
            if (heartbeatDate.HasValue)
            {
                command.Parameters.AddWithValue("@HeartbeatDate", heartbeatDate.Value);
            }

            command.ExecuteNonQuery();
            return new TransactionId((long)tranId.Value);
        }

        public void CommitTransaction(TransactionId transactionId, string failureReason = null, bool isWatchDog = false)
        {
            using var connection = GetConnection((ShardId?)null);
            using var command = new SqlCommand("dbo.CommitTransaction", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            command.Parameters.AddWithValue("@TransactionId", transactionId.Id);
            command.Parameters.AddWithValue("@FailureReason", failureReason);
            command.Parameters.AddWithValue("@IsWatchDog", isWatchDog);
            command.ExecuteNonQuery();
        }

        public void PutTransactionHeartbeat(TransactionId transactionId, DateTime? explicitDate)
        {
            using var connection = GetConnection((ShardId?)null);
            using var command = new SqlCommand("dbo.PutTransactionHeartbeat", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            command.Parameters.AddWithValue("@TransactionId", transactionId.Id);
            if (explicitDate.HasValue)
            {
                command.Parameters.AddWithValue("@HeartbeatDate", explicitDate.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@HeartbeatDate", DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}
