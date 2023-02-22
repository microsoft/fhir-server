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

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlRetryServiceTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private const int SqlConnectionError = 233;
        private const int SqlLoginError = 4060;
        private const int SqlDivByZeroErrorNumber = 8134;
        private const int SqlDefaultErrorNumber = 50000;

        public SqlRetryServiceTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

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

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionError_SingleRetryIsRun()
        {
            await SingleConnectionRetryTest(SqlConnectionError, CreateTestStoredProcedureWithSingleConnectionError);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionError_AllRetriesAreRun()
        {
            await AllConnectionRetriesTest(SqlConnectionError, CreateTestStoredProcedureWithAllConnectionErrors);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_SingleRetryIsRun()
        {
            await SingleConnectionRetryTest(SqlLoginError, CreateTestStoredProcedureToReadTop10, true);
        }

        [Fact]
        public async Task GivenSqlCommandFunc_WhenConnectionInitializationError_AllRetriesAreRun()
        {
            await AllConnectionRetriesTest(SqlLoginError, CreateTestStoredProcedureToReadTop10, true);
        }

        private async Task TestInitializationExecuteSql(string commandText)
        {
            using SqlConnection sqlConnection = await _fixture.SqlConnectionBuilder.GetSqlConnectionAsync();
            using SqlCommand sqlCommand = sqlConnection.CreateCommand();
            await sqlConnection.OpenAsync();
            sqlCommand.CommandText = commandText;
            await sqlCommand.ExecuteNonQueryAsync();
        }

        private async Task CreateTestStoredProcedureToReadTop10(string storedProcedureName, string tableName)
        {
            await TestInitializationExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
SELECT TOP 10 RowId = row_number() OVER (ORDER BY object_id) FROM sys.objects
             ");
        }

        private async Task CreateTestStoredProcedureWithAllConnectionErrors(string storedProcedureName, string tableName)
        {
            await TestInitializationExecuteSql(@$"
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
            await TestInitializationExecuteSql(@$"
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
            await TestInitializationExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
RAISERROR('TestError', 20, 127) WITH LOG
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleFatalError(string storedProcedureName, string tableName)
        {
            await TestInitializationExecuteSql(@$"
CREATE OR ALTER PROCEDURE dbo.{storedProcedureName}
AS
set nocount on
DECLARE @RaiseError bit = 0
IF NOT EXISTS (SELECT * FROM dbo.{tableName})
BEGIN
  INSERT INTO dbo.{tableName} (Id) SELECT 'TestError' 
  RAISERROR('TestError', 20, 127) WITH LOG
END
             ");
        }

        private async Task CreateTestStoredProcedureWithSingleErrorBeforeSelect(string storedProcedureName, string tableName)
        {
            await TestInitializationExecuteSql(@$"
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
            await TestInitializationExecuteSql(@$"
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
            await TestInitializationExecuteSql(@$"
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
                await TestInitializationExecuteSql(@$"CREATE TABLE dbo.{tableName} (Id varchar(100) PRIMARY KEY)");
            }
        }

        private async Task DropTestObjecs(string storedProcedureName, string tableName)
        {
            if (tableName != null)
            {
                await TestInitializationExecuteSql(@$"IF object_id('dbo.{tableName}') IS NOT NULL DROP TABLE dbo.{tableName}");
            }

            await TestInitializationExecuteSql($"IF object_id('dbo.{storedProcedureName}') IS NOT NULL DROP PROCEDURE dbo.{storedProcedureName}");
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

        private async Task<SqlRetryService> InitializeTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, string storedProcedureName, string testTableName, bool testConnectionInitializationFailure = false, bool failOnAllRetries = false)
        {
            await CreateTestTable(testTableName);
            await testStoredProc(storedProcedureName, testTableName);

            var sqlRetryServiceOptions = new SqlRetryServiceOptions();
            sqlRetryServiceOptions.AddTransientErrors.Add(sqlErrorNumber);
            sqlRetryServiceOptions.RetryMillisecondsDelay = 10;
            sqlRetryServiceOptions.MaxRetries = 3;
            if (testConnectionInitializationFailure)
            {
                return new SqlRetryService(new SqlConnectionBuilderWithConnectionInitializationFailure(_fixture.SqlConnectionBuilder, failOnAllRetries), Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
            }
            else
            {
                return new SqlRetryService(_fixture.SqlConnectionBuilder, Microsoft.Extensions.Options.Options.Create(sqlRetryServiceOptions), new SqlRetryServiceDelegateOptions());
            }
        }

        private async Task SingleConnectionRetryTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure = false)
        {
            MakeStoredProcedureAndTableName(false, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName, testConnectionInitializationFailure);
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
                Assert.Equal(sqlErrorNumber, ((SqlException)logger.LogRecords[0].Exception).Number);
            }
            finally
            {
                await DropTestObjecs(storedProcedureName, testTableName);
            }
        }

        private async Task AllConnectionRetriesTest(int sqlErrorNumber, Func<string, string, Task> testStoredProc, bool testConnectionInitializationFailure = false)
        {
            MakeStoredProcedureAndTableName(true, out string storedProcedureName, out string testTableName);
            try
            {
                SqlRetryService sqlRetryService = await InitializeTest(sqlErrorNumber, testStoredProc, storedProcedureName, testTableName, testConnectionInitializationFailure, true);
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
                TestInitializationExecuteSql($"KILL {sessionId}").Wait();
            }

            return rowId;
        }

        /*private class SqlCommandFuncWithRetriesObject
        {
            private readonly SqlRetryServiceTests _sqlRetryServiceTests;
            private readonly string _storedProcedureName;
            private readonly bool _killConnectionOnAllRetries;
            private int _retryCount;

            public SqlCommandFuncWithRetriesObject(SqlRetryServiceTests sqlRetryServiceTests, string storedProcedureName, bool killConnectionOnAllRetries = false)
            {
                _sqlRetryServiceTests = sqlRetryServiceTests;
                _storedProcedureName = storedProcedureName;
                _killConnectionOnAllRetries = killConnectionOnAllRetries;
                _retryCount = 0;
            }

            public async Task SqlCommandActionWithRetries(SqlCommand sqlCommand, CancellationToken cancellationToken)
            {
                sqlCommand.CommandText = $"dbo.{_storedProcedureName}";

                await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            public async Task<List<long>> SqlCommandFuncWithRetries(SqlCommand sqlCommand, CancellationToken cancellationToken)
            {
                sqlCommand.CommandText = $"dbo.{_storedProcedureName}";

                using SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(cancellationToken);
                var results = new List<long>();
                while (await sqlDataReader.ReadAsync(cancellationToken))
                {
                    long r = sqlDataReader.GetInt64(0);
                    results.Add(r);
                }

                await sqlDataReader.NextResultAsync(cancellationToken);

                return results;
            }

            public async Task<List<long>> SqlCommandFuncWithRetriesAndKillConnection(SqlCommand sqlCommand, CancellationToken cancellationToken)
            {
                bool killSent = false;
                _retryCount++;

                sqlCommand.CommandText = $"dbo.{_storedProcedureName}";

                using SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(cancellationToken);
                var results = new List<long>();
                while (await sqlDataReader.ReadAsync(cancellationToken))
                {
                    long r = sqlDataReader.GetInt64(0); // Get SQL session ID.
                    results.Add(r);

                    if (_killConnectionOnAllRetries || _retryCount == 1)
                    {
                        // On this try/retry we are killing the connection!
                        if (!killSent) // Send KILL command to the server only once!
                        {
                            killSent = true;

                            // Among other things, kills the connection from the server side. Once we are done reading the current buffer and go to the network we encounter broken connection condition.
                            await _sqlRetryServiceTests.TestInitializationExecuteSql($"KILL {r}");
                        }
                    }
                    else
                    {
                        // If we are not killing the connection on this try/retry no need to read the whole table from the server. Just read the first row and end.
                        break;
                    }
                }

                await sqlDataReader.NextResultAsync(cancellationToken);

                return results;
            }
        }*/

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
