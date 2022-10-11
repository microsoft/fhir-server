// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlDataReaderExtensionsTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly string _connectionString;

        public SqlDataReaderExtensionsTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _connectionString = fixture.TestConnectionString;
        }

        [Fact]
        public void GivenASqlDataReader_WhenReadingFieldsWithCorrectNamesAndOrdinals_ReturnsCorrectData()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                SqlCommand sqlCommand = connection.CreateCommand();
                sqlCommand.CommandText = @"
                    DECLARE @int32 int = 1
                    DECLARE @int64 bigint = 1
                    DECLARE @string varchar(3) = 'abc'
                    DECLARE @datetime datetime2 = '1968-10-23 12:45:37'
                    DECLARE @datetimeoffset datetimeoffset = '1968-10-23 12:45:37 +10:0'
                    DECLARE @bool bit = 1
                    DECLARE @byte tinyint = 1
                    DECLARE @int16 smallint = 1
                    DECLARE @decimal decimal = 1
                    DECLARE @double float(53) = 1.0
                    DECLARE @single float(24) = 1.0
                    DECLARE @bytes varbinary(max) = 0x1
                    DECLARE @guid uniqueidentifier = '91f994e5-f34c-4b8a-a09d-36de9fa82924'

                    SELECT  @int32 as int32,
                            @int64 as int64,
                            @string as string,
                            @datetime as datetime,
                            @datetimeoffset as datetimeoffset,
                            @bool as bool,
                            @byte as byte,
                            @int16 as int16,
                            @decimal as decimal,
                            @double as [double],
                            @single as single,
                            @bytes as bytes,
                            @guid as guid";

                SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
                Assert.True(sqlDataReader.Read());

                Assert.Equal(1, sqlDataReader.GetInt32("int32", 0));
                Assert.Equal(1, sqlDataReader.GetValue("int32", 0));
                Assert.False(sqlDataReader.IsDBNull("int32", 0));

                Assert.Equal(1, sqlDataReader.GetInt64("int64", 1));
                Assert.Equal("abc", sqlDataReader.GetString("string", 2));
                Assert.Equal(
                    DateTime.SpecifyKind(new DateTime(1968, 10, 23, 12, 45, 37), DateTimeKind.Utc),
                    sqlDataReader.GetDateTime("datetime", 3));
                Assert.Equal(
                    new DateTimeOffset(new DateTime(1968, 10, 23, 12, 45, 37), TimeSpan.FromHours(10)),
                    sqlDataReader.GetDateTimeOffset("datetimeoffset", 4));

                Assert.True(sqlDataReader.GetBoolean("bool", 5));
                Assert.Equal(1, sqlDataReader.GetByte("byte", 6));
                Assert.Equal(1, sqlDataReader.GetInt16("int16", 7));
                Assert.Equal(1, sqlDataReader.GetDecimal("decimal", 8));
                Assert.Equal(1.0, sqlDataReader.GetDouble("double", 9));
                Assert.Equal(1.0, sqlDataReader.GetFloat("single", 10));
                byte[] actual = new byte[10];
                byte[] expectedBytes = { 1 };

                Assert.Equal(expectedBytes.Length, sqlDataReader.GetBytes("bytes", 11, 0, actual, 0, actual.Length));
                Assert.Equal(expectedBytes, actual.Take(expectedBytes.Length));

                Assert.Equal(expectedBytes.Length, sqlDataReader.GetStream("bytes", 11).Read(actual, 0, actual.Length));
                Assert.Equal(expectedBytes, actual.Take(expectedBytes.Length));

                Assert.Equal(Guid.Parse("91f994e5-f34c-4b8a-a09d-36de9fa82924"), sqlDataReader.GetGuid("guid", 12));
            }
        }

#if DEBUG // checks are only enabled on debug builds.
        [Fact(Skip = "Won't work with nuget package. Needs to be moved to shared repo.")]
        public void GivenASqlDataReader_WhenReadingFieldsWithIncorrectCorrectNamesAndOrdinals_Throws()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                SqlCommand sqlCommand = connection.CreateCommand();
                sqlCommand.CommandText = @"select 1 as int";

                SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
                Assert.True(sqlDataReader.Read());

                Assert.Throws<InvalidOperationException>(() => sqlDataReader.GetInt32("WrongName", 0));
                Assert.Throws<InvalidOperationException>(() => sqlDataReader.GetInt64("int", 0));
            }
        }
#endif
    }
}
