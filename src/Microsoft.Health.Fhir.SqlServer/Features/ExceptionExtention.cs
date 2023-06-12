// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Microsoft.Health.Fhir.SqlServer.Features
{
    internal static class ExceptionExtention
    {
        internal static bool IsRetriable(this Exception e)
        {
            var str = e.ToString().ToLowerInvariant();
            return HasNetworkErrorPattern(str)
                   || HasInternalSqlErrorPattern(str)
                   || HasDatabaseAvailabilityPattern(str)
                   || HasDatabaseOverloadPattern(str)
                   || HasDeadlockErrorPattern(str)
                   || HasIncorrectAsyncCallPattern(str);
        }

        internal static bool IsExecutionTimeout(this Exception e)
        {
            var str = e.ToString().ToLowerInvariant();
            return str.Contains("execution timeout expired", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDeadlockErrorPattern(string str)
        {
            return str.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNetworkErrorPattern(string str)
        {
            return str.Contains("semaphore timeout", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connected host has failed to respond", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("operation on a socket could not be performed", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection is closed", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("severe error occurred", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection timeout expired", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("existing connection was forcibly closed by the remote host", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection was recovered and rowcount in the first query is not available", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection was successfully established with the server, but then an error occurred during the login process", StringComparison.OrdinalIgnoreCase);

            ////A severe error occurred on the current command.  The results, if any, should be discarded.
            ////Meaning:
            ////The service has encountered an error processing your request. Please try again. Error code %d. You will receive this error,
            ////when the service is down due to software or hardware upgrades, hardware failures, or any other failover problems. Reconnecting to your
            ////SQL Database server will automatically connect you to a healthy copy of your database. You may see error codes 40143 and 40166 embedded
            ////within the message of error 40540. The error codes 40143 and 40166 provide additional information about the kind of failover that occurred.
            ////Do not modify your application to catch error codes 40143 and 40166. Your application should catch 40540 and try reconnecting to SQL Database
            ////until the resources are available and your connection is established again.
            ////4083: The connection was recovered and rowcount in the first query is not available. Please execute another query to get a valid rowcount.
            ////Connection Timeout Expired.The timeout period elapsed during the post - login phase
            ////transport connection: An existing connection was forcibly closed by the remote host
        }

        private static bool HasInternalSqlErrorPattern(string str)
        {
            return (str.Contains("app domain", StringComparison.OrdinalIgnoreCase) && str.Contains("was unloaded due to memory pressure", StringComparison.OrdinalIgnoreCase))
                   || (str.Contains("remote procedure call", StringComparison.OrdinalIgnoreCase) && str.Contains("protocol stream is incorrect", StringComparison.OrdinalIgnoreCase))
                   || (str.Contains("service has encountered an error processing your request", StringComparison.OrdinalIgnoreCase) && str.Contains("try again", StringComparison.OrdinalIgnoreCase));

            ////The app domain with specified version id (59) was unloaded due to memory pressure and could not be found
            ////The incoming tabular data stream(TDS) remote procedure call(RPC) protocol stream is incorrect.Parameter 3("?"): Data type 0x00 is unknown.
            ////The service has encountered an error processing your request. Please try again. Error code 823.
        }

        private static bool HasDatabaseAvailabilityPattern(string str)
        {
            return (str.Contains("unable to access database", StringComparison.OrdinalIgnoreCase) && str.Contains("high availability", StringComparison.OrdinalIgnoreCase))
                    || str.Contains("error occurred while establishing a connection", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connection is broken and recovery is not possible", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("is not currently available", StringComparison.OrdinalIgnoreCase)
                    || (str.Contains("availability replica", StringComparison.OrdinalIgnoreCase) && str.Contains("ghost records are being deleted", StringComparison.OrdinalIgnoreCase))
                    || (str.Contains("the definition of object", StringComparison.OrdinalIgnoreCase) && str.Contains("has changed since it was compiled", StringComparison.OrdinalIgnoreCase))
                    || str.Contains("object accessed by the statement has been modified by a ddl statement", StringComparison.OrdinalIgnoreCase)
                    || (str.Contains("transaction log for database", StringComparison.OrdinalIgnoreCase) && str.Contains("is full due to", StringComparison.OrdinalIgnoreCase))
                    || str.Contains("has reached its size quota", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("connections to this database are no longer allowed", StringComparison.OrdinalIgnoreCase) // happened on SLO update from HS_Gen5_16 to HS_Gen4_1
                    || str.Contains("database is in emergency mode", StringComparison.OrdinalIgnoreCase)
                    || (str.Contains("transaction log for database", StringComparison.OrdinalIgnoreCase) && str.Contains("full due to 'ACTIVE_BACKUP_OR_RESTORE'", StringComparison.OrdinalIgnoreCase))
                    || str.Contains("Login failed for user", StringComparison.OrdinalIgnoreCase)
                    || str.Contains("The timeout period elapsed prior to obtaining a connection from the pool", StringComparison.OrdinalIgnoreCase);

            ////Unable to access database 'VS_Prod_008_v1' because it lacks a quorum of nodes for high availability. Try the operation again later.
            ////A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.)
            ////The connection is broken and recovery is not possible. The connection is marked by the server as unrecoverable. No attempt was made to restore the connection.
            ////Database 'VS_TLN_Prod_004_v0' on server 'tln-sql' is not currently available.  Please retry the connection later.
            ////The transaction was terminated because of the availability replica config/state change or because ghost records are being deleted on the primary and the secondary availability replica
            ////The definition of object 'SelectPrimitivesByPrimitiveIds' has changed since it was compiled. - this happens on store setup
            ////Error 3961: Snapshot isolation transaction failed in database 'VS_Prod_007_v5' because the object accessed by the statement has been modified by a DDL statement in another concurrent transaction since the start of this transaction.It is disallowed because the metadata is not versioned.A concurrent update to metadata can lead to inconsistency if mixed with snapshot isolation.
            ////The transaction log for database '7bc46349-b716-46df-b0f4-f1c1a39163b9' is full due to 'AVAILABILITY_REPLICA'
            ////The database 'VS_SG_009_v0' has reached its size quota.Partition or delete data, drop indexes, or consult the documentation for possible resolutions.
            ////Error 3908: Could not run BEGIN TRANSACTION in database 'VS_SG_015_v0' because the database is in emergency mode or is damaged and must be restarted.
            ////Error 3906: Failed to update database XXX because the database is read-only. TODO: Remove when HS team fixes the problem
            ////The transaction log for database '0c7fbc7e-651f-4b64-938f-96efe8ae20ce' is full due to 'ACTIVE_BACKUP_OR_RESTORE'
        }

        private static bool HasDatabaseOverloadPattern(string str)
        {
            return str.Contains("request limit for the database", StringComparison.OrdinalIgnoreCase) && str.Contains("has been reached", StringComparison.OrdinalIgnoreCase);

            ////The request limit for the database is 200 and has been reached.
        }

        // TODO: Remove when source of this exception is identified
        private static bool HasIncorrectAsyncCallPattern(string str)
        {
            return str.Contains("This method may not be called when another read operation is pending", StringComparison.OrdinalIgnoreCase);
        }
    }
}
