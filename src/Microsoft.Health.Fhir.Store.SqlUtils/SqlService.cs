// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.SqlUtils
{
    public class SqlService
    {
        private string _connectionString;
        private string _secondaryConnectionString;

        public SqlService(string connectionString, string secondaryConnectionString = null)
        {
            _connectionString = connectionString;
            _secondaryConnectionString = secondaryConnectionString;
        }

        public string ConnectionString => GetTrueConnectionString();

        public string DatabaseName => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

        public SqlConnection GetConnection()
        {
            return GetConnection(ConnectionString);
        }

        public static SqlConnection GetConnection(string connectionString, int connectionTimeoutSec = 600)
        {
            var retriesSql = 0;
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var connection = new SqlConnection(connectionString);
                try
                {
                    connection.Open();
                    return connection;
                }
                catch (SqlException e)
                {
                    // We have to retry the connection even if the exception is "Login failed", because
                    // SQL Azure can throw this exception when a database changes scale or physical location.
                    var prefix = $"GetConnection.[server={connection.DataSource};database={connection.Database}]: RetriesSQL={retriesSql++}: ";
                    connection.Dispose();
                    if (e.IsRetryable() || e.ToString().Contains("login failed", StringComparison.OrdinalIgnoreCase))
                    {
                        sw.Restart();
                    }
                    else if (sw.Elapsed.TotalSeconds > connectionTimeoutSec)
                    {
                        throw;
                    }

                    Thread.Sleep(5000);
                }
                catch (InvalidOperationException e)
                {
                    connection.Dispose();
                    if (!e.IsRetryable()) // not retriable
                    {
                        throw;
                    }

                    Thread.Sleep(5000);
                }
            }
        }

        internal static void ExecuteWithRetries(Action action)
        {
            while (true)
            {
                try
                {
                    action();
                    break;
                }
                catch (SqlException e)
                {
                    if (e.IsRetryable())
                    {
                        Thread.Sleep(ExceptionExtention.RetryWaitMillisecond);
                        continue;
                    }

                    throw;
                }
            }
        }

        public static void ExecuteSqlWithRetries(string connectionString, SqlCommand cmd, Action<SqlCommand> action, int connectionTimeoutSec = 600)
        {
            while (true)
            {
                try
                {
                    using var connection = GetConnection(connectionString, connectionTimeoutSec);
                    cmd.Connection = connection;
                    action(cmd);
                    break;
                }
                catch (SqlException e)
                {
                    if (e.IsRetryable())
                    {
                        Thread.Sleep(ExceptionExtention.RetryWaitMillisecond);
                        continue;
                    }

                    throw;
                }
            }
        }

        public void ExecuteSqlWithRetries(SqlCommand cmd, Action<SqlCommand> action, int connectionTimeoutSec = 600)
        {
            ExecuteSqlWithRetries(_connectionString, cmd, action, connectionTimeoutSec);
        }

        public static void ExecuteSqlReaderWithRetries(string connectionString, SqlCommand cmd, Action<SqlDataReader> action, int connectionTimeoutSec = 600)
        {
            ExecuteSqlWithRetries(
                connectionString,
                cmd,
                cmdInt =>
                {
                    using var reader = cmdInt.ExecuteReader();
                    action(reader);
                    reader.NextResult();
                },
                connectionTimeoutSec);
        }

        public static IList<T> ExecuteSqlReaderWithRetries<T>(string connectionString, SqlCommand cmd, Func<SqlDataReader, T> toT, int connectionTimeoutSec = 600)
        {
            IList<T> results = null;
            ExecuteSqlWithRetries(
                connectionString,
                cmd,
                cmdInt =>
                {
                    using var reader = cmdInt.ExecuteReader();
                    results = new List<T>();
                    while (reader.Read())
                    {
                        results.Add(toT(reader));
                    }

                    reader.NextResult();
                },
                connectionTimeoutSec);
            return results;
        }

        public IList<T> ExecuteSqlReaderWithRetries<T>(SqlCommand cmd, Func<SqlDataReader, T> toT, int connectionTimeoutSec = 600)
        {
            return ExecuteSqlReaderWithRetries(_connectionString, cmd, toT, connectionTimeoutSec);
        }

        public void LogEvent(string process, string status, string mode, string target = null, string action = null, long? rows = null, DateTime? startTime = null, string text = null)
        {
            using var conn = GetConnection(ConnectionString);
            using var command = new SqlCommand("dbo.LogEvent", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@Process", process);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Mode", mode);
            if (target != null)
            {
                command.Parameters.AddWithValue("@Target", target);
            }

            if (action != null)
            {
                command.Parameters.AddWithValue("@Action", action);
            }

            if (rows != null)
            {
                command.Parameters.AddWithValue("@Rows", rows);
            }

            if (startTime != null)
            {
                command.Parameters.AddWithValue("@Start", startTime);
            }

            if (text != null)
            {
                command.Parameters.AddWithValue("@Text", text);
            }

            command.ExecuteNonQuery();
        }

        public string ShowConnectionString()
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            return $"server={builder.DataSource};database={builder.InitialCatalog}";
        }

        public static string GetCanonicalConnectionString(string connectionString)
        {
            var connStr = connectionString.Replace("Trust Server Certificate=True", string.Empty, StringComparison.OrdinalIgnoreCase);
            return connStr;
        }

        public static string ShowConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return $"server={builder.DataSource};database={builder.InitialCatalog}";
        }

        public string GetTrueConnectionString(bool? useSecondaryStore = null)
        {
            if (!useSecondaryStore.HasValue || _secondaryConnectionString == null)
            {
                return _connectionString;
            }

            return useSecondaryStore.Value ? _secondaryConnectionString : _connectionString;
        }
    }
}
