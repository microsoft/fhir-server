// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

public class SimpleSqlConnectionBuilder : ISqlConnectionBuilder, IDisposable
{
    private readonly string _connectionString;
    private readonly IList<WeakReference<SqlConnection>> _connections = new List<WeakReference<SqlConnection>>();

    public SimpleSqlConnectionBuilder(string connectionString)
    {
        _connectionString = connectionString;
    }

    public Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, CancellationToken cancellationToken = default)
    {
        var connectionBuilder = new SqlConnectionStringBuilder(_connectionString);

        if (!string.IsNullOrEmpty(initialCatalog))
        {
            connectionBuilder.InitialCatalog = initialCatalog;
        }

        // Pooling is causing issues when deleting the test database at the end of the fixture run.
        connectionBuilder.Pooling = false;

        var result = new SqlConnection(connectionBuilder.ToString());
        _connections.Add(new WeakReference<SqlConnection>(result));
        return Task.FromResult(result);
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            if (connection.TryGetTarget(out SqlConnection target))
            {
                if (target.State == ConnectionState.Open)
                {
                    Debug.WriteLine("WARNING: Connection was not closed.");
                    target.Close();
                }

                target.Dispose();
            }
        }
    }
}
