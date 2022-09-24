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
        public TransactionId BeginTransaction(string definition = null, DateTime? heartbeatDate = null)
        {
            using var command = new SqlCommand("dbo.BeginTransaction") { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            var tranId = new SqlParameter("@TransactionId", SqlDbType.BigInt);
            tranId.Direction = ParameterDirection.Output;
            command.Parameters.Add(tranId);
            command.Parameters.AddWithValue("@Definition", definition);
            command.Parameters.AddWithValue("@ReturnRecordSet", false);
            command.Parameters.AddWithValue("@TimeoutSeconds", 600);
            if (heartbeatDate.HasValue)
            {
                command.Parameters.AddWithValue("@HeartbeatDate", heartbeatDate.Value);
            }

            ExecuteSqlWithRetries(null, command, cmd => cmd.ExecuteNonQuery());
            return new TransactionId((long)tranId.Value);
        }

        public void CommitTransaction(TransactionId transactionId, string failureReason = null, bool isWatchDog = false)
        {
            using var command = new SqlCommand("dbo.CommitTransaction") { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            command.Parameters.AddWithValue("@TransactionId", transactionId.Id);
            command.Parameters.AddWithValue("@FailureReason", failureReason);
            command.Parameters.AddWithValue("@IsWatchDog", isWatchDog);
            ExecuteSqlWithRetries(null, command, cmd => cmd.ExecuteNonQuery());
        }

        public void PutTransactionHeartbeat(TransactionId transactionId, DateTime? explicitDate)
        {
            using var command = new SqlCommand("dbo.PutTransactionHeartbeat") { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            command.Parameters.AddWithValue("@TransactionId", transactionId.Id);
            if (explicitDate.HasValue)
            {
                command.Parameters.AddWithValue("@HeartbeatDate", explicitDate.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@HeartbeatDate", DBNull.Value);
            }

            ExecuteSqlWithRetries(null, command, cmd => cmd.ExecuteNonQuery());
        }

        public int AdvanceTransactionVisibility()
        {
            using var command = new SqlCommand("dbo.AdvanceTransactionVisibility") { CommandType = CommandType.StoredProcedure, CommandTimeout = 240 };
            var affected = new SqlParameter("@AffectedRows", SqlDbType.Int);
            affected.Direction = ParameterDirection.Output;
            command.Parameters.Add(affected);
            ExecuteSqlWithRetries(null, command, cmd => cmd.ExecuteNonQuery());
            return (int)affected.Value;
        }
    }
}
