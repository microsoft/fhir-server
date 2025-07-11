// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Antlr4.Runtime.Misc;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlExceptionExtensionsTests
    {
        [Theory]
        [MemberData(nameof(GetSqlTransientCheckData))]
        public void GivenSqlException_WhenCheckingTransiency_ThenIsSqlTransientExceptionShouldReturnCorrectValue(
            int errorNumber,
            HashSet<int> transientErrors,
            bool isTransient)
        {
            var exception = CreateSqlException(errorNumber);
            Assert.Equal(isTransient, exception.IsSqlTransientException(transientErrors));
        }

        private static SqlException CreateSqlException(int errorNumber)
        {
            var error = Create<SqlError>(
                errorNumber,
                (byte)0,
                (byte)0,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                null);
            var errorCollection = Create<SqlErrorCollection>();
            typeof(SqlErrorCollection)
                .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(errorCollection, new object[] { error });

            var exception = typeof(SqlException)
                .GetMethod(
                    "CreateException",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    CallingConventions.ExplicitThis,
                    new[] { typeof(SqlErrorCollection), typeof(string) },
                    new ParameterModifier[] { })
                .Invoke(null, new object[] { errorCollection, "7.0.0" }) as SqlException;
            return exception;
        }

        private static T Create<T>(params object[] p)
        {
            var ctors = typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            return (T)ctors.First(ctor => ctor.GetParameters().Length == p.Length).Invoke(p);
        }

        public static IEnumerable<object[]> GetSqlTransientCheckData()
        {
            var data = new[]
            {
                new object[]
                {
                    42108,
                    null,
                    true,
                },
                new object[]
                {
                    9999,
                    null,
                    false,
                },
                new object[]
                {
                    3,
                    new HashSet<int> { 1, 2, 3, 4, 5 },
                    true,
                },
                new object[]
                {
                    10,
                    new HashSet<int> { 1, 2, 3, 4, 5 },
                    false,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
