// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerColumnTypeChangeTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerColumnTypeChangeTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void GivenColumnTypeChange_InsertAndSelectWork()
        {
            try
            {
                // int in the database
                ExecuteSql("CREATE TABLE dbo.TestTbl (Id int NOT NULL, Col int NOT NULL)");
                ExecuteSql("CREATE TYPE dbo.TestList AS TABLE (Id int NOT NULL, Col int NOT NULL)");

                Insert([new TestInt(1, 1), new TestInt(2, 2)]);
                Insert([new TestLong(3, 3), new TestLong(4, 4)]);
                Insert([new TestString(5, "5"), new TestString(6, "6")]);
                var rowsInt = Select((reader) => new TestInt(reader.GetInt32(0), reader.GetInt32(1))).ToList();
                Assert.Equal(6, rowsInt.Count);
                var rowsLong = Select((reader) => new TestLong(reader.GetInt32(0), reader.GetInt32(1))).ToList();
                Assert.Equal(6, rowsInt.Count);
                var rowsString = Select((reader) => new TestString(reader.GetInt32(0), reader.GetInt32(1).ToString())).ToList();
                Assert.Equal(6, rowsString.Count);

                // long in the database
                ExecuteSql("DROP TABLE dbo.TestTbl");
                ExecuteSql("DROP TYPE dbo.TestList");
                ExecuteSql("CREATE TABLE dbo.TestTbl (Id int NOT NULL, Col bigint NOT NULL)");
                ExecuteSql("CREATE TYPE dbo.TestList AS TABLE (Id int NOT NULL, Col bigint NOT NULL)");

                Insert([new TestInt(1, 1), new TestInt(2, 2)]);
                Insert([new TestLong(3, 3), new TestLong(4, 4)]);
                Insert([new TestString(5, "5"), new TestString(6, "6")]);
                rowsInt = Select((reader) => new TestInt(reader.GetInt32(0), (int)reader.GetInt64(1))).ToList();
                Assert.Equal(6, rowsInt.Count);
                rowsLong = Select((reader) => new TestLong(reader.GetInt32(0), reader.GetInt64(1))).ToList();
                Assert.Equal(6, rowsInt.Count);
                rowsString = Select((reader) => new TestString(reader.GetInt32(0), reader.GetInt64(1).ToString())).ToList();
                Assert.Equal(6, rowsString.Count);

                // string in the database
                ExecuteSql("DROP TABLE dbo.TestTbl");
                ExecuteSql("DROP TYPE dbo.TestList");
                ExecuteSql("CREATE TABLE dbo.TestTbl (Id int NOT NULL, Col varchar(64) NOT NULL)");
                ExecuteSql("CREATE TYPE dbo.TestList AS TABLE (Id int NOT NULL, Col varchar(64) NOT NULL)");

                Insert([new TestInt(1, 1), new TestInt(2, 2)]);
                Insert([new TestLong(3, 3), new TestLong(4, 4)]);
                Insert([new TestString(5, "5"), new TestString(6, "6")]);
                rowsInt = Select((reader) => new TestInt(reader.GetInt32(0), int.Parse(reader.GetString(1)))).ToList();
                Assert.Equal(6, rowsInt.Count);
                rowsLong = Select((reader) => new TestLong(reader.GetInt32(0), long.Parse(reader.GetString(1)))).ToList();
                Assert.Equal(6, rowsInt.Count);
                rowsString = Select((reader) => new TestString(reader.GetInt32(0), reader.GetString(1))).ToList();
                Assert.Equal(6, rowsString.Count);
            }
            finally
            {
                ExecuteSql("IF object_id('dbo.TestTbl') IS NOT NULL DROP TABLE dbo.TestTbl");
                ExecuteSql("IF EXISTS (SELECT * FROM sys.types WHERE name = 'TestList') DROP TYPE dbo.TestList");
            }
        }

        private void ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private IEnumerable<T> Select<T>(Func<SqlDataReader, T> toT)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT * FROM dbo.TestTbl", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }

        private void Insert(IEnumerable<TestInt> rows)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("INSERT INTO dbo.TestTbl SELECT * FROM @TestList", conn);
            var param = new SqlParameter { ParameterName = "@TestList" };
            param.AddTestIntList(rows);
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }

        private void Insert(IEnumerable<TestLong> rows)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("INSERT INTO dbo.TestTbl SELECT * FROM @TestList", conn);
            var param = new SqlParameter { ParameterName = "@TestList" };
            param.AddTestLongList(rows);
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }

        private void Insert(IEnumerable<TestString> rows)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("INSERT INTO dbo.TestTbl SELECT * FROM @TestList", conn);
            var param = new SqlParameter { ParameterName = "@TestList" };
            param.AddTestStringList(rows);
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }
    }

#pragma warning disable SA1402 // File may only contain a single type
    public class TestString
    {
        public TestString(SqlDataReader reader)
        {
            Id = reader.GetInt32(0);
            Col = reader.GetString(1);
        }

        public TestString(int id, string col)
        {
            Id = id;
            Col = col;
        }

        public int Id { get; }

        public string Col { get; }
    }

    public class TestInt
    {
        public TestInt(SqlDataReader reader)
        {
            Id = reader.GetInt32(0);
            Col = reader.GetInt32(1);
        }

        public TestInt(int id, int col)
        {
            Id = id;
            Col = col;
        }

        public int Id { get; }

        public int Col { get; }
    }

    public class TestLong
    {
        public TestLong(SqlDataReader reader)
        {
            Id = reader.GetInt32(0);
            Col = reader.GetInt64(1);
        }

        public TestLong(int id, long col)
        {
            Id = id;
            Col = col;
        }

        public int Id { get; }

        public long Col { get; }
    }

    public static class SqlParamaterTestIntExtension
    {
        static SqlParamaterTestIntExtension()
        {
            MetaData = [new SqlMetaData("Id", SqlDbType.Int), new SqlMetaData("Col", SqlDbType.Int)];
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTestIntList(this SqlParameter param, IEnumerable<TestInt> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TestList";
            param.Value = GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TestInt> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt32(0, row.Id);
                record.SetSqlInt32(1, row.Col);
                yield return record;
            }
        }
    }

    public static class SqlParamaterTestLongExtension
    {
        static SqlParamaterTestLongExtension()
        {
            MetaData = [new SqlMetaData("Id", SqlDbType.Int), new SqlMetaData("Col", SqlDbType.BigInt)];
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTestLongList(this SqlParameter param, IEnumerable<TestLong> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TestList";
            param.Value = GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TestLong> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt32(0, row.Id);
                record.SetSqlInt64(1, row.Col);
                yield return record;
            }
        }
    }

    public static class SqlParamaterTestStringExtension
    {
        static SqlParamaterTestStringExtension()
        {
            MetaData = [new SqlMetaData("Id", SqlDbType.Int), new SqlMetaData("Col", SqlDbType.VarChar, 64)];
        }

        private static SqlMetaData[] MetaData { get; }

        public static void AddTestStringList(this SqlParameter param, IEnumerable<TestString> rows)
        {
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TestList";
            param.Value = GetSqlDataRecords(rows);
        }

        private static IEnumerable<SqlDataRecord> GetSqlDataRecords(IEnumerable<TestString> rows)
        {
            var record = new SqlDataRecord(MetaData);
            foreach (var row in rows)
            {
                record.SetSqlInt32(0, row.Id);
                record.SetSqlString(1, row.Col);
                yield return record;
            }
        }
    }
}
