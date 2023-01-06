// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Internal.IndexRebuilder
{
    internal sealed class IndexRebuilder
    {
        private readonly string _connectionString;
        private readonly int _threads;
        private readonly bool _rebuildClustered;

        internal IndexRebuilder(string connStr, int threads, bool rebuildClustered)
        {
            _connectionString = connStr;
            _threads = threads;
            _rebuildClustered = rebuildClustered;
        }

        internal void Run(out CancelRequest cancel, out int numberOfTables)
        {
            SwitchPartitionsOutAllTables(_rebuildClustered);
            var commands = GetCommandsForRebuildIndexes(_rebuildClustered);
            numberOfTables = commands.Select(_ => _.Table).Distinct().Count();
            cancel = RunCommands(commands);
            if (cancel.IsSet)
            {
                return;
            }

            if (_rebuildClustered) // do other indexes because others were already done before
            {
                commands = GetCommandsForRebuildIndexes(false);
                numberOfTables = commands.Select(_ => _.Table).Distinct().Count();
                cancel = RunCommands(commands);
                if (cancel.IsSet)
                {
                    return;
                }
            }

            SwitchPartitionsInAllTables();
        }

        private CancelRequest RunCommands(IList<(string Table, IList<string> SqlCommands)> commands)
        {
            var cancelInt = new CancelRequest();
            BatchExtensions.ParallelForEach(
                commands,
                _threads,
                (thread, sqlPlus) =>
                {
                    if (sqlPlus.SqlCommands.Any(_ => _.EndsWith("REBUILD", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var rebuildIndex in sqlPlus.SqlCommands)
                        {
                            ExecuteSqlCommand(sqlPlus.Table, rebuildIndex, cancelInt);
                        }
                    }
                    else
                    {
                        var cmd = sqlPlus.SqlCommands.Single(); // there should be single cmd in the list
                        ExecuteSqlCommand(sqlPlus.Table, cmd, cancelInt);
                    }
                },
                cancelInt);

            return cancelInt;
        }

        private IList<(string Table, IList<string> SqlCommands)> GetCommandsForRebuildIndexes(bool rebuildClustered) // Item1 is Table name, Items - list of SQL commands in the order they have to be executed
        {
            var resultsDic = new Dictionary<string, List<string>>();
            var tablesWithPreservedOrder = new List<string>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var command = new SqlCommand("dbo.GetCommandsForRebuildIndexes", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
                command.Parameters.AddWithValue("@RebuildClustered", rebuildClustered);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var table = reader.GetString(0);
                    var sql = reader.GetString(1);
                    if (resultsDic.TryGetValue(table, out List<string> value))
                    {
                        value.Add(sql);
                    }
                    else
                    {
                        tablesWithPreservedOrder.Add(table);
                        resultsDic.Add(table, new List<string> { sql });
                    }
                }
            }

            var results = new List<(string table, IList<string> commands)>();
            foreach (var table in tablesWithPreservedOrder)
            {
                // unbundle index creates
                if (resultsDic[table].Any(_ => _.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var cmd in resultsDic[table])
                    {
                        results.Add((table, new List<string> { cmd }));
                    }
                }
                else
                {
                    results.Add((table, resultsDic[table]));
                }
            }

            return results;
        }

        private void SwitchPartitionsInAllTables()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.SwitchPartitionsInAllTables", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.ExecuteNonQuery();
        }

        private void SwitchPartitionsOutAllTables(bool rebuildClustered)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var command = new SqlCommand("dbo.SwitchPartitionsOutAllTables", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
            command.Parameters.AddWithValue("@RebuildClustered", rebuildClustered);
            command.ExecuteNonQuery();
        }

        private void ExecuteSqlCommand(string tbl, string cmd, CancelRequest cancel)
        {
            if (cancel.IsSet)
            {
                return;
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var command = new SqlCommand("dbo.ExecuteCommandForRebuildIndexes", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
                command.Parameters.AddWithValue("@Tbl", tbl);
                command.Parameters.AddWithValue("@Cmd", cmd);
                command.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                cancel.Set();
                throw;
            }
        }
    }
}
