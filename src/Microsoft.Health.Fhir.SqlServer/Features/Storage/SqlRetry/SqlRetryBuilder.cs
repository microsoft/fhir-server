// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer;
using Polly;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage;

public record SqlRetryBuilder
{
    private readonly PolicyBuilder _policy;
    private readonly ISqlConnectionBuilder _connectionBuilder;
    private readonly TimeSpan _commandTimeout;
    private readonly byte _maxRetries;
    private readonly TimeSpan _retryDelay;

    public SqlRetryBuilder(PolicyBuilder policy, ISqlConnectionBuilder connectionBuilder, TimeSpan commandTimeout, byte maxRetries, TimeSpan retryDelay)
    {
        EnsureArg.IsNotNull(policy, nameof(policy));
        EnsureArg.IsNotNull(connectionBuilder, nameof(connectionBuilder));

        _policy = policy;
        _connectionBuilder = connectionBuilder;
        _commandTimeout = commandTimeout;
        _maxRetries = maxRetries;
        _retryDelay = retryDelay;
    }

    /// <summary>
    /// Specifies the type of exception that this policy can handle with additional filters on this exception type.
    /// </summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="exceptionPredicate">The exception predicate to filter the type of exception this policy can handle.</param>
    /// <returns>The PolicyBuilder instance.</returns>
    public SqlRetryBuilder Handle<TException>(Func<TException, bool> exceptionPredicate = null)
        where TException : Exception
    {
        if (exceptionPredicate != null)
        {
            _policy.Or(exceptionPredicate);
        }
        else
        {
            _policy.Or<TException>();
        }

        return this;
    }

    public SqlRetryBuilder With(Action<PolicyBuilder> policy)
    {
        EnsureArg.IsNotNull(policy, nameof(policy))
            .Invoke(_policy);
        return this;
    }

    public async Task ExecuteAsync(SqlCommand sqlCommand, Func<SqlCommand, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
        EnsureArg.IsNotNull(action, nameof(action));

        await _policy
            .WaitAndRetryAsync(_maxRetries, i => _retryDelay)
            .ExecuteAsync(async () =>
            {
                await using SqlConnection sqlConnection = await _connectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                await SetupCommand(sqlCommand, sqlConnection, cancellationToken);
                await action.Invoke(sqlCommand, cancellationToken);
            });
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(action, nameof(action));

        await _policy
            .WaitAndRetryAsync(_maxRetries, i => _retryDelay)
            .ExecuteAsync(action, cancellationToken);
    }

    public async Task ExecuteNonQueryAsync(
        SqlCommand sqlCommand,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));

        await _policy
            .WaitAndRetryAsync(_maxRetries, i => _retryDelay)
            .ExecuteAsync(async () =>
            {
                await using SqlConnection sqlConnection = await _connectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                await SetupCommand(sqlCommand, sqlConnection, cancellationToken);
                await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
            });
    }

    public Task<List<TResult>> ExecuteReaderAsync<TResult>(
        SqlCommand sqlCommand,
        Func<SqlDataReader, TResult> readerToResult,
        CancellationToken cancellationToken)
    {
        return ExecuteReaderAsync(sqlCommand, readerToResult, true, cancellationToken);
    }

    public async Task<TResult> ExecuteSingleAsync<TResult>(
        SqlCommand sqlCommand,
        Func<SqlDataReader, TResult> readerToResult,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
        EnsureArg.IsNotNull(readerToResult, nameof(readerToResult));

        List<TResult> results = await ExecuteReaderAsync(sqlCommand, readerToResult, false, cancellationToken);

        return results?.Count > 0 ? results[0] : default;
    }

    private async Task<List<TResult>> ExecuteReaderAsync<TResult>(
        SqlCommand sqlCommand,
        Func<SqlDataReader, TResult> readerToResult,
        bool readAllRows,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
        EnsureArg.IsNotNull(readerToResult, nameof(readerToResult));

        return await _policy
            .WaitAndRetryAsync(_maxRetries, i => _retryDelay)
            .ExecuteAsync(async () =>
            {
                await using SqlConnection sqlConnection = await _connectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                await SetupCommand(sqlCommand, sqlConnection, cancellationToken);

                await using SqlDataReader reader = await sqlCommand.ExecuteReaderAsync(cancellationToken);

                var results = new List<TResult>();
                bool firstRow = true;

                while (await reader.ReadAsync(cancellationToken) && (readAllRows || firstRow))
                {
                    results.Add(readerToResult(reader));
                    firstRow = false;
                }

                await reader.NextResultAsync(cancellationToken);

                return results;
            });
    }

    private async Task SetupCommand(SqlCommand sqlCommand, SqlConnection connection, CancellationToken cancellationToken)
    {
        // Connection is never opened by the _sqlConnectionBuilder but RetryLogicProvider is set to the old, depreciated retry implementation. According to the .NET spec, RetryLogicProvider
        // must be set before opening connection to take effect. Therefore we must reset it to null here before opening the connection.
        connection.RetryLogicProvider = null; // To remove this line _sqlConnectionBuilder in healthcare-shared-components must be modified.
        await connection.OpenAsync(cancellationToken);

        sqlCommand.CommandTimeout = (int)_commandTimeout.TotalSeconds;
        sqlCommand.Connection = connection;
    }
}
