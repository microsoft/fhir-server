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
        public async Task GivenSqlCommandAction_WhenErrorAfterSelect_AllRetriesAreRun()
        {
            await AllRetriesTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithAllFatalErrors, false);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenErrorAfterSelect_AllRetriesAreRun()
        {
            await AllRetriesTest(SqlDefaultErrorNumber, CreateTestStoredProcedureWithAllFatalErrors, true);
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
            await SingleConnectionRetryTest(CreateTestStoredProcedureWithSingleConnectionError);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionError_AllRetriesAreRun()
        {
            await AllConnectionRetriesTest(CreateTestStoredProcedureWithAllConnectionErrors);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_SingleRetryIsRun()
        {
            await SingleConnectionRetryTest(CreateTestStoredProcedureToReadTop10, true);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_AllRetriesAreRun()
        {
            await AllConnectionRetriesTest(CreateTestStoredProcedureToReadTop10, true);
        }

        private async Task ExecuteSql(string commandText)
        {
            using SqlConnection sqlConnection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync();
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

        private async Task<SqlRetryService> InitializeTest(int? sqlErrorNumber, Func<string, string, Task> testStoredProc, string storedProcedureName, string testTableName, bool testConnectionInitializationFailure = false, bool failOnAllRetries = false)
        {
            await CreateTestTable(testTableName);
            await testStoredProc(storedProcedureName, testTableName);

            var sqlRetryServiceOptions = new SqlRetryServiceOptions();
            if (sqlErrorNumber != null)
            {
                sqlRetryServiceOptions.AddTransientErrors.Add((int)sqlErrorNumber);
            }

            sqlRetryServiceOptions.RetryMillisecondsDelay = 10;
            sqlRetryServiceOptions.MaxRetries = 3;
            if (testConnectionInitializationFailure)
            {
                return new SqlRetryService(new SqlConnectionBuilderWithConnectionInitializationFailure(_fixture.SqlConnectionBuilder, failOnAllRetries), _fixture.SqlServerDataStoreConfiguration, Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
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

        private async Task SingleConnectionRetryTest(Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure = false)
        {
            MakeStoredProcedureAndTableName(false, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(null, testStoredProc, storedProcedureName, testTableName, testConnectionInitializationFailure);
                var logger = new TestLogger();

                using SqlCommand sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";
                List<long> result = await sqlRetryService.ExecuteSqlDataReader<long, SqlRetryService>(
                    sqlCommand,
                    testConnectionInitializationFailure ? SqlCommandFuncWithRetries : SqlCommandFuncWithRetriesAndKillConnection,
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
                Assert.IsType<SqlException>(logger.LogRecords[0].Exception);
                Assert.True(IsConnectionFailedException(logger.LogRecords[0].Exception, testConnectionInitializationFailure));
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task AllConnectionRetriesTest(Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure = false)
        {
            MakeStoredProcedureAndTableName(true, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(null, testStoredProc, storedProcedureName, testTableName, testConnectionInitializationFailure, true);
                var logger = new TestLogger();

                using SqlCommand sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";
                SqlException ex;
                ex = await Assert.ThrowsAsync<SqlException>(() => sqlRetryService.ExecuteSqlDataReader<long, SqlRetryService>(
                    sqlCommand,
                    testConnectionInitializationFailure ? SqlCommandFuncWithRetries : SqlCommandFuncWithRetriesAndKillConnection,
                    logger,
                    "log message",
                    CancellationToken.None));

                Assert.True(IsConnectionFailedException(ex, testConnectionInitializationFailure));
                Assert.Equal(3, logger.LogRecords.Count);

                Assert.Equal(LogLevel.Information, logger.LogRecords[0].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[0].Exception);
                Assert.True(IsConnectionFailedException(logger.LogRecords[0].Exception, testConnectionInitializationFailure));

                Assert.Equal(LogLevel.Information, logger.LogRecords[1].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[1].Exception);
                Assert.True(IsConnectionFailedException(logger.LogRecords[1].Exception, testConnectionInitializationFailure));

                Assert.Equal(LogLevel.Error, logger.LogRecords[2].LogLevel);
                Assert.IsType<SqlException>(logger.LogRecords[2].Exception);
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
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName);
                var logger = new TestLogger();

                using SqlCommand sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";

                if (func)
                {
                    List<long> result = await sqlRetryService.ExecuteSqlDataReader(
                        sqlCommand,
                        SqlCommandFuncWithRetries,
                        logger,
                        "log message",
                        CancellationToken.None);

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

        private async Task AllRetriesTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, bool func)
        {
            MakeStoredProcedureAndTableName(true, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName);
                var logger = new TestLogger();

                using SqlCommand sqlCommand = new SqlCommand();
                sqlCommand.CommandText = $"dbo.{storedProcedureName}";

                SqlException ex;
                if (func)
                {
                    ex = await Assert.ThrowsAsync<SqlException>(() => sqlRetryService.ExecuteSqlDataReader(
                        sqlCommand,
                        SqlCommandFuncWithRetries,
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

        private long SqlCommandFuncWithRetries(SqlDataReader sqlDataReader)
        {
            return sqlDataReader.GetInt64(0);
        }

        private long SqlCommandFuncWithRetriesAndKillConnection(SqlDataReader sqlDataReader)
        {
            long rowId = sqlDataReader.GetInt64(0);
            long sessionId = sqlDataReader.GetInt64(1);
            if (rowId == 0)
            {
                ExecuteSql($"KILL {sessionId}").Wait();
            }

            return rowId;
        }

        private class SqlConnectionBuilderWithConnectionInitializationFailure : ISqlConnectionBuilder
        {
            private readonly bool _failOnAllRetries;
            private int _retryCount;
            private readonly ISqlConnectionBuilder _sqlConnectionBuilder;

            public SqlConnectionBuilderWithConnectionInitializationFailure(ISqlConnectionBuilder sqlConnectionBuilder, bool failOnAllRetries)
            {
                _sqlConnectionBuilder = sqlConnectionBuilder;
                _failOnAllRetries = failOnAllRetries;
            }

            public async Task<SqlConnection> GetSqlConnectionAsync(string initialCatalog = null, CancellationToken cancellationToken = default)
            {
                _retryCount++;
                if (_failOnAllRetries || _retryCount == 1)
                {
                    return new SqlConnection("Data Source=(local);Initial Catalog=FHIRINTEGRATIONTEST_DATABASE_DOES_NOT_EXIST;Integrated Security=True;Trust Server Certificate=True");
                }
                else
                {
                    return await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog, cancellationToken);
                }
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
