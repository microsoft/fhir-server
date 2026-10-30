// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Failures shared by background job classification tests.
    /// </summary>
    public static class JobFailureTestData
    {
        /// <summary>
        /// Gets failure scenarios and their expected customer classification.
        /// </summary>
        public static IEnumerable<object[]> ClassificationCases()
        {
            yield return new object[] { "cmk", true };
            yield return new object[] { "wrapped-cmk", true };
            yield return new object[] { "parameters", true };
            yield return new object[] { "wrapped-parameters", true };
            yield return new object[] { "timeout", false };
            yield return new object[] { "wrapped-timeout", false };
            yield return new object[] { "translated-timeout", false };
            yield return new object[] { "mixed", false };
            yield return new object[] { "conflict", true };
            yield return new object[] { "wrapped-conflict", true };
            yield return new object[] { "customer-and-cancel", true };
            yield return new object[] { "customer-and-operation-canceled", false };
            yield return new object[] { "cancellation-with-cmk", false };
            yield return new object[] { "cancel-only", false };
            yield return new object[] { "empty-aggregate", false };
            yield return new object[] { "unrelated-invalid-request", false };
        }

        /// <summary>
        /// Gets Export failures, excluding its existing direct job-record conflict path.
        /// </summary>
        public static IEnumerable<object[]> ExportClassificationCases() =>
            ClassificationCases().Where(testCase => (string)testCase[0] != "conflict");

        /// <summary>
        /// Creates a failure with a realistic nested incomplete-operation chain when requested.
        /// </summary>
        public static Exception CreateException(string scenario)
        {
            return scenario switch
            {
                "cmk" => new CustomerManagedKeyException("Customer-managed key is inaccessible.", CreateSqlException(33183, "SQL key vault error.")),
                "wrapped-cmk" => Wrap(CreateException("cmk")),
                "parameters" => new RequestNotValidException(Core.Resources.TooManyParameters),
                "wrapped-parameters" => Wrap(CreateException("parameters")),
                "timeout" => CreateSqlException(-2, "SQL operation timed out."),
                "wrapped-timeout" => Wrap(CreateException("timeout")),
                "translated-timeout" => new RequestTimeoutException("SQL operation timed out."),
                "mixed" => Wrap(new AggregateException(CreateException("cmk"), CreateException("timeout"))),
                "conflict" => new Core.Features.Operations.JobConflictException("A reindex job is currently running."),
                "wrapped-conflict" => Wrap(CreateException("conflict")),
                "customer-and-cancel" => Wrap(new AggregateException(CreateException("cmk"), new TaskCanceledException())),
                "customer-and-operation-canceled" => Wrap(new AggregateException(CreateException("cmk"), new OperationCanceledException())),
                "cancellation-with-cmk" => new OperationCanceledException("Operation canceled.", CreateException("cmk")),
                "cancel-only" => new AggregateException(new TaskCanceledException()),
                "empty-aggregate" => new AggregateException(),
                "unrelated-invalid-request" => new RequestNotValidException("Unrelated invalid request."),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }

        private static Exception Wrap(Exception exception)
        {
            return new IncompleteOperationException<IDictionary<string, long>>(
                new AggregateException(new IncompleteOperationException<Dictionary<string, long>>(exception, new Dictionary<string, long>())),
                new Dictionary<string, long>());
        }

        private static SqlException CreateSqlException(int number, string message)
        {
            var error = (SqlError)typeof(SqlError)
                .GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception) },
                    null)
                .Invoke(new object[] { number, (byte)0, (byte)0, "server", message, "procedure", 0, null });
            var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true);
            typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(errors, new object[] { error });
            return (SqlException)Activator.CreateInstance(
                typeof(SqlException),
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new object[] { message, errors, null, Guid.NewGuid() },
                null);
        }
    }
}
