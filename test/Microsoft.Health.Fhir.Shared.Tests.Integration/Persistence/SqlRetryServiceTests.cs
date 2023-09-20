// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlRetryServiceTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const int SqlDivByZeroErrorNumber = 8134;
        private const int SqlDefaultErrorNumber = 50000;

        public SqlRetryServiceTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // SQL fatal error tests.

        [Fact]
        public async Task GivenSqlCommandAction_WhenFatalError_SingleRetryIsRun()
        {
            await SingleRetryTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithSingleFatalError, false, 0);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenFatalError_SingleRetryIsRun()
        {
            await SingleRetryTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithSingleFatalError, true, 0);
        }

        [Fact]
        public async Task GivenSqlCommandAction_WhenErrorAfterSelect_AllRetriesFail()
        {
            await AllRetriesFailTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithAllFatalErrors, false);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenErrorAfterSelect_AllRetriesFail()
        {
            await AllRetriesFailTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithAllFatalErrors, true);
        }

        // SQL SELECT retry tests.

        [Fact]
        public async Task GivenSqlCommandFunc_WhenErrorBeforeSelect_SingleRetryIsRun()
        {
            await SingleRetryTest(SqlDivByZeroErrorNumber, CreateTestStoredProcedureWithSingleErrorBeforeSelect, true, 10);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenErrorInSelect_SingleRetryIsRun()
        {
            await SingleRetryTest(SqlDivByZeroErrorNumber, CreateTestStoredProcedureWithSingleErrorInSelect, true, 10);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenErrorAfterSelect_SingleRetryIsRun()
        {
            await SingleRetryTest(SqlDivByZeroErrorNumber, CreateTestStoredProcedureWithSingleErrorAfterSelect, true, 10);
        }

        // Connection error retry tests.

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionError_SingleRetryIsRun()
        {
            await SingleConnectionRetryTest(CreateTestStoredProcedureWithSingleConnectionError, false);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionError_AllRetriesFail()
        {
            await AllConnectionRetriesTest(CreateTestStoredProcedureWithAllConnectionErrors, false);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_SingleRetryIsRun()
        {
            await SingleConnectionRetryTest(CreateTestStoredProcedureToReadTop10, true);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_AllRetriesFail()
        {
            await AllConnectionRetriesTest(CreateTestStoredProcedureToReadTop10, true);
        }

        private async Task ExecuteSql(string commandText)
        {
            using SqlConnection sqlConnection = _fixture.SqlConnectionBuilder.GetSqlConnection();
            using SqlCommand sqlCommand = sqlConnection.CreateCommand();
            await sqlConnection.OpenAsync();
            sqlCommand.CommandText = commandText;
            await sqlCommand.ExecuteNonQueryAsync();
        }

        private async Task CreateTestStoredProcedureToReadTop10(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id) FROM sys.objects
             ");
        }

        private async Task CreateTestStoredProcedureWithAllConnectionErrors(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
-- Use CROSS JOIN to make sure there's enough data to fill at least one comm buffer so test code has to go to the network again after the buffer is read. This is when network exception will happen.
-- Row with RowId = 0 will trigger network exception logic.
SELECT RowId = row_number() OVER (ORDER BY X.object_id) - 1, CAST(@@SPID AS BIGINT) AS 'SessionId', SYSTEM_USER AS 'Login Name', USER AS 'User Name', * FROM sys.objects CROSS JOIN sys.objects AS X
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleConnectionError(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError'
-- Use CROSS JOIN to make sure there's enough data to fill at least one comm buffer so test code has to go to the network again after the buffer is read. This is when network exception will happen.
-- Row with RowId = 0 will trigger network exception logic.
  SELECT RowId = row_number() OVER (ORDER BY X.object_id) - 1, CAST(@@SPID AS BIGINT) AS 'SessionId', SYSTEM_USER AS 'Login Name', USER AS 'User Name', * FROM sys.objects CROSS JOIN sys.objects AS X
END
ELSE
BEGIN
-- In the case of no comm error, no need to send large amount of data to fill the client comm buffer.
  SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id), CAST(@@SPID AS BIGINT) AS 'SessionId' FROM sys.objects
END
             ");
        }

        private async Task CreateTestStoredProcedureWithAllFatalErrors(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
RAISERROR('TestError', 18, 127)
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleFatalError(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
DECLARE @RaiseError bit = 0
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError' 
  RAISERROR('TestError', 18, 127)
END
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleErrorBeforeSelect(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
DECLARE @RaiseError bit = 0
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError' 
  SET @RaiseError = 1 / 0
END
SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id) FROM sys.objects
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleErrorInSelect(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
DECLARE @RaiseError bit = 0
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError' 
  SET @RaiseError = 1
END
SELECT RowId / CASE WHEN RowId = 6 AND @RaiseError = 1 THEN 0 ELSE 1 END -- conditionally raise error on row 6
  FROM (SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id) FROM sys.objects) A
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleErrorAfterSelect(string storedProcedureName, string tableName)
        {
            await ExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
DECLARE @RaiseError bit = 0
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError' 
  SET @RaiseError = 1
END
SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id) FROM sys.objects
IF @RaiseError = 1
BEGIN
  SET @RaiseError = 1 / 0
END
             ");
        }

        private async Task CreateTestTable(string tableName)
        {
            if (tableName != null)
            {
                await ExecuteSql(@$"CREATE TABLE dbo.{tableName} (Id varchar(100) PRIMARY KEY)");
            }
        }

        private async Task DropTestObjecs(string storedProcedureName, string tableName)
        {
            if (tableName != null)
            {
                await ExecuteSql(@$"IF object_id('dbo.{tableName}') IS NOT NULL DROP TABLE dbo.{tableName}");
            }

            await ExecuteSql($"IF object_id('dbo.{storedProcedureName}') IS NOT NULL DROP PROCEDURE dbo.{storedProcedureName}");
        }

        private void MakeStoredProcedureAndTableName(bool allRetriesFail, out string storedProcedureName, out string testTableName)
        {
            string rnd = Guid.NewGuid().ToString().ComputeHash()[..14].ToLower();
            storedProcedureName = "SqlRetryTestSProc" + rnd;
            if (allRetriesFail)
            {
                testTableName = null;
            }
            else
            {
                testTableName = "SqlRetryTestTbl" + rnd;
            }
        }

        private async Task<SqlRetryService> InitializeTest(int? sqlErrorNumber, Func<string, string, Task> testStoredProc, string storedProcedureName, string testTableName, bool testConnectionNoPooling, bool testConnectionInitializationFailure, bool testConnectionInitializationAllRetriesFail)
        {
            await CreateTestTable(testTableName);
            await testStoredProc(storedProcedureName, testTableName);

            var sqlRetryServiceOptions = new SqlRetryServiceOptions();
            if (sqlErrorNumber != null)
            {
                sqlRetryServiceOptions.AddTransientErrors.Add((int)sqlErrorNumber);
            }

            sqlRetryServiceOptions.RetryMillisecondsDelay = 200;
            sqlRetryServiceOptions.MaxRetries = 3;
            if (testConnectionNoPooling)
            {
                return new SqlRetryService(new SqlConnectionBuilderNoPooling(_fixture.SqlConnectionBuilder), _fixture.SqlServerDataStoreConfiguration, Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
            }
            else if (testConnectionInitializationFailure)
            {
                return new SqlRetryService(new SqlConnectionBuilderWithConnectionInitializationFailure(_fixture.SqlConnectionBuilder, testConnectionInitializationAllRetriesFail), _fixture.SqlServerDataStoreConfiguration, Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
            }
            else
            {
                return new SqlRetryService(_fixture.SqlConnectionBuilder, _fixture.SqlServerDataStoreConfiguration, Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
            }
        }

        private bool IsConnectionFailedException(Exception ex, bool testConnectionInitializationFailure)
        {
            if (ex is SqlException sqlEx)
            {
                if (testConnectionInitializationFailure)
                {
                    if (sqlEx.Number == 4060)
                    {
                        // On Windows we get correct error number.
                        return true;
                    }
                    else if (sqlEx.Number == 0 && sqlEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) && sqlEx.Message.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        // On Linux we get 0 error number for various connection problems so we check message string as well.
                        return true;
                    }
                }
                else
                {
                    if (sqlEx.Number == 233)
                    {
                        // On Windows we get correct error number.
                        return true;
                    }
                    else if (sqlEx.Number == 0 && sqlEx.Message.Contains("transport", StringComparison.OrdinalIgnoreCase) && sqlEx.Message.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        // On Linux we get 0 error number for various connection problems so we check message string as well.
                        return true;
                    }
                }
            }
            else if (ex != null)
            {
                if (!testConnectionInitializationFailure)
                {
                    // On Linux sometimes non-SqlException is thrown.
                    if (ex is InvalidOperationException invOpEx && invOpEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) && invOpEx.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            _output.WriteLine("Error, unrecognized exception.");
            if (ex == null)
            {
                _output.WriteLine("  Exception: null");
            }
            else
            {
                _output.WriteLine($"  Exception type: {ex.GetType().FullName}");
                _output.WriteLine($"  Exception message: {ex.Message}");
                if (ex is SqlException sqlException)
                {
                    _output.WriteLine($"  Exception number: {sqlException.Number}");
                }

                Exception innerEx = ex.InnerException;
                if (innerEx == null)
                {
                    _output.WriteLine("    Inner exception: null");
                }
                else
                {
                    _output.WriteLine($"    Inner exception type: {innerEx.GetType().FullName}");
                    _output.WriteLine($"    Inner exception message: {innerEx.Message}");
                }
            }

            return false;
        }

        private async Task SingleConnectionRetryTest(Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure)
        {
            MakeStoredProcedureAndTableName(false, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(null, testStoredProc, storedProcedureName, testTableName, !testConnectionInitializationFailure, testConnectionInitializationFailure, false);
                var logger = new TestLogger();

                using var sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";
                var result = await sqlRetryService.ExecuteReaderAsync<long, SqlRetryService>(
                    sqlCommand,
                    testConnectionInitializationFailure ? ReaderToResult : ReaderToResultAndKillConnection,
                    logger,
                    "log message",
                    CancellationToken.None);

                const int resultCount = 10;
                Assert.Equal(resultCount, result.Count);
                for (int i = 0; i < resultCount; i++)
                {
                    Assert.Equal(i + 1, result[i]);
                }

                Assert.Single(logger.LogRecords);

                Assert.Equal(LogLevel.Information, logger.LogRecords[0].LogLevel);
                Assert.True(IsConnectionFailedException(logger.LogRecords[0].Exception, testConnectionInitializationFailure));
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task AllConnectionRetriesTest(Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure)
        {
            MakeStoredProcedureAndTableName(true, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(null, testStoredProc, storedProcedureName, testTableName, !testConnectionInitializationFailure, testConnectionInitializationFailure, true);
                var logger = new TestLogger();

                using var sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";
                try
                {
                    _output.WriteLine($"{DateTime.Now:O}: Start executing ExecuteSqlDataReader.");
                    await sqlRetryService.ExecuteReaderAsync<long, SqlRetryService>(
                        sqlCommand,
                        testConnectionInitializationFailure ? ReaderToResult : ReaderToResultAndKillConnection,
                        logger,
                        "log message",
                        CancellationToken.None);
                    _output.WriteLine($"{DateTime.Now:O}: End executing ExecuteSqlDataReader.");

                    // In this test exception should be thrown. This point should never be reached.
                    // We use this pattern instead of Assert.ThrowsAsync<T> because different exception types could be thrown if connection is broken (SqlException, InvalidOperationException).
                    Assert.True(false);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"{DateTime.Now:O}: ExecuteSqlDataReader throws.");
                    Assert.True(IsConnectionFailedException(ex, testConnectionInitializationFailure));
                }

                Assert.Equal(3, logger.LogRecords.Count);

                Assert.Equal(LogLevel.Information, logger.LogRecords[0].LogLevel);
                Assert.True(IsConnectionFailedException(logger.LogRecords[0].Exception, testConnectionInitializationFailure));

                Assert.Equal(LogLevel.Information, logger.LogRecords[1].LogLevel);
                Assert.True(IsConnectionFailedException(logger.LogRecords[1].Exception, testConnectionInitializationFailure));

                Assert.Equal(LogLevel.Error, logger.LogRecords[2].LogLevel);
                Assert.True(IsConnectionFailedException(logger.LogRecords[2].Exception, testConnectionInitializationFailure));
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task SingleRetryTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, bool func, int resultCount)
        {
            MakeStoredProcedureAndTableName(false, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName, false, false, false);
                var logger = new TestLogger();

                using var sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";

                if (func)
                {
                    var result = await sqlCommand.ExecuteReaderAsync(
                        sqlRetryService,
                        ReaderToResult,
                        logger,
                        CancellationToken.None,
                        "log message");

                    Assert.Equal(resultCount, result.Count);
                    for (int i = 0; i < resultCount; i++)
                    {
                        Assert.Equal(i + 1, result[i]);
                    }
                }
                else
                {
                    await sqlRetryService.ExecuteSql(
                        sqlCommand,
                        SqlCommandActionWithRetries,
                        logger,
                        "log message",
                        CancellationToken.None);
                }

                Assert.Single(logger.LogRecords);

                Assert.Equal(LogLevel.Information, logger.LogRecords[0].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[0].Exception);
                Assert.Equal(sqlErrorNumber, ((SqlException)logger.LogRecords[0].Exception).Number);
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task AllRetriesFailTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, bool func)
        {
            MakeStoredProcedureAndTableName(true, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName, false, false, false);
                var logger = new TestLogger();

                using var sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";

                SqlException ex;
                if (func)
                {
                    ex = await Assert.ThrowsAsync<SqlException>(() => sqlRetryService.ExecuteReaderAsync(
                        sqlCommand,
                        ReaderToResult,
                        logger,
                        "log message",
                        CancellationToken.None));
                }
                else
                {
                    ex = await Assert.ThrowsAsync<SqlException>(() => sqlRetryService.ExecuteSql(
                        sqlCommand,
                        SqlCommandActionWithRetries,
                        logger,
                        "log message",
                        CancellationToken.None));
                }

                Assert.Equal(sqlErrorNumber, ex.Number);
                Assert.Equal(3, logger.LogRecords.Count);

                Assert.Equal(LogLevel.Information, logger.LogRecords[0].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[0].Exception);
                Assert.Equal(sqlErrorNumber, ((SqlException)logger.LogRecords[0].Exception).Number);

                Assert.Equal(LogLevel.Information, logger.LogRecords[1].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[1].Exception);
                Assert.Equal(sqlErrorNumber, ((SqlException)logger.LogRecords[1].Exception).Number);

                Assert.Equal(LogLevel.Error, logger.LogRecords[2].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[2].Exception);
                Assert.Equal(sqlErrorNumber, ((SqlException)logger.LogRecords[2].Exception).Number);
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task SqlCommandActionWithRetries(SqlCommand sqlCommand, CancellationToken cancellationToken)
        {
            await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        private long ReaderToResult(SqlDataReader sqlDataReader)
        {
            return sqlDataReader.GetInt64(0);
        }

        private long ReaderToResultAndKillConnection(SqlDataReader sqlDataReader)
        {
            long rowId = sqlDataReader.GetInt64(0);
            long sessionId = sqlDataReader.GetInt64(1);
            if (rowId == 0)
            {
                _output.WriteLine($"{DateTime.Now:O}: Start KillConnection.");
                KillConnection(sessionId);
                _output.WriteLine($"{DateTime.Now:O}: End KillConnection.");
            }

            return rowId;
        }

        private void KillConnection(long sessionId)
        {
            // We kill the SQL connection in a separate thread and using a separate SQL connection, not from task thread pool and SQL
            // connection pool. Otherwise when running on Linux we may encounter a deadlock situation with test timing out and causing
            // vstest process to terminate with the following stack trace:
            // --->System.Exception: Unable to read beyond the end of the stream.
            //    at System.IO.BinaryReader.Read7BitEncodedInt()
            //    at System.IO.BinaryReader.ReadString()
            //    at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.LengthPrefixCommunicationChannel.NotifyDataAvailable()
            //    at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TcpClientExtensions.MessageLoopAsync(TcpClient client, ICommunicationChannel channel, Action`1 errorHandler, CancellationToken cancellationToken)
            //    -- - End of inner exception stack trace-- -.

            var t = new Thread(new ThreadStart(() =>
            {
                _output.WriteLine($"{DateTime.Now:O}: Start KillConnection thread.");
                using SqlConnection sqlConnection = _fixture.SqlConnectionBuilder.GetSqlConnection();
                var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString)
                {
                    Pooling = false,
                };
                sqlConnection.ConnectionString = sqlConnectionStringBuilder.ConnectionString;
                using SqlCommand sqlCommand = sqlConnection.CreateCommand();
                sqlConnection.Open();
                sqlCommand.CommandText = $"KILL {sessionId}";
                sqlCommand.ExecuteNonQuery();
                _output.WriteLine($"{DateTime.Now:O}: End KillConnection thread.");
            }));
            t.Start();
            Thread.Sleep(0);
            t.Join();
        }

        private class SqlConnectionBuilderWithConnectionInitializationFailure : ISqlConnectionBuilder
        {
            private readonly bool _allRetriesFail;
            private int _retryCount;
            private readonly ISqlConnectionBuilder _sqlConnectionBuilder;

            public SqlConnectionBuilderWithConnectionInitializationFailure(ISqlConnectionBuilder sqlConnectionBuilder, bool allRetriesFail)
            {
                _sqlConnectionBuilder = sqlConnectionBuilder;
                _allRetriesFail = allRetriesFail;
            }

            public string DefaultDatabase => _sqlConnectionBuilder.DefaultDatabase;

            public SqlConnection GetSqlConnection(string initialCatalog = null, int? maxPoolSize = null)
            {
                SqlConnection sqlConnection = _sqlConnectionBuilder.GetSqlConnection(initialCatalog, null);
                _retryCount++;
                if (_allRetriesFail || _retryCount == 1)
                {
                    var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString)
                    {
                        InitialCatalog = "FHIRINTEGRATIONTEST_DATABASE_DOES_NOT_EXIST",
                    };
                    sqlConnection.ConnectionString = sqlConnectionStringBuilder.ConnectionString;
                }

                return sqlConnection;
            }

            public async Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, int? maxPoolSize = null, CancellationToken cancellationToken = default)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                SqlConnection sqlConnection = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog, null, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
                _retryCount++;
                if (_allRetriesFail || _retryCount == 1)
                {
                    var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString)
                    {
                        InitialCatalog = "FHIRINTEGRATIONTEST_DATABASE_DOES_NOT_EXIST",
                    };
                    sqlConnection.ConnectionString = sqlConnectionStringBuilder.ConnectionString;
                }

                return sqlConnection;
            }
        }

        private class SqlConnectionBuilderNoPooling : ISqlConnectionBuilder
        {
            private readonly ISqlConnectionBuilder _sqlConnectionBuilder;

            public SqlConnectionBuilderNoPooling(ISqlConnectionBuilder sqlConnectionBuilder)
            {
                _sqlConnectionBuilder = sqlConnectionBuilder;
            }

            public string DefaultDatabase => _sqlConnectionBuilder.DefaultDatabase;

            public SqlConnection GetSqlConnection(string initialCatalog = null, int? maxPoolSize = null)
            {
                SqlConnection sqlConnection = _sqlConnectionBuilder.GetSqlConnection(initialCatalog, null);
                var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString)
                {
                    Pooling = false,
                };
                sqlConnection.ConnectionString = sqlConnectionStringBuilder.ConnectionString;
                return sqlConnection;
            }

            public async Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, int? maxPoolSize = null, CancellationToken cancellationToken = default)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                SqlConnection sqlConnection = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog, null, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
                var sqlConnectionStringBuilder = new SqlConnectionStringBuilder(sqlConnection.ConnectionString)
                {
                    Pooling = false,
                };
                sqlConnection.ConnectionString = sqlConnectionStringBuilder.ConnectionString;
                return sqlConnection;
            }
        }

        private class TestLogger : ILogger<SqlRetryService>
        {
            internal List<LogRecord> LogRecords { get; } = new List<LogRecord>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                LogRecords.Add(new LogRecord() { LogLevel = logLevel, Exception = exception });
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return false;
            }

            internal class LogRecord
            {
                internal LogLevel LogLevel { get; init; }

                internal Exception Exception { get; init; }
            }
        }
    }
}
