// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    public static class SqlExceptionFactory
    {
        public static SqlException GetSqlException(string message, Exception innerException)
        {
            var errorCollection = (SqlErrorCollection)Activator.CreateInstance(
                typeof(SqlErrorCollection),
                true);

            var sqlException = (SqlException)Activator.CreateInstance(
                typeof(SqlException),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object[] { message, errorCollection, innerException, Guid.NewGuid() },
                null);

            return sqlException;
        }

        public static SqlException GetSqlException(int number, string message)
        {
            SqlError error = GetSqlExceptionWithSqlError(number, message);

            return GetSqlException(new[] { error });
        }

        public static SqlException GetSqlException(SqlError[] errors)
        {
            // Use reflection to create SqlException with custom errors
            var errorCollection = (SqlErrorCollection)Activator.CreateInstance(
                typeof(SqlErrorCollection),
                true);

            foreach (var error in errors)
            {
                typeof(SqlErrorCollection)
                    .GetMethod("Add", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(errorCollection, new object[] { error });
            }

            var sqlException = (SqlException)Activator.CreateInstance(
                typeof(SqlException),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object[] { "Test SQL exception", errorCollection, null, Guid.NewGuid() },
                null);

            return sqlException;
        }

        public static SqlError GetSqlExceptionWithSqlError(int number, string message)
        {
            // Use reflection to create SqlError
            return (SqlError)typeof(SqlError)
                .GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception) },
                    null)
                .Invoke(new object[] { number, (byte)0, (byte)0, "server", message, "proc", 0, null });
        }
    }
}
