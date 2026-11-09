// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
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
            yield return new object[] { "unknown-wrapper-conflict", false };
            yield return new object[] { "unknown-wrapper-cancellation", false };
            yield return new object[] { "customer-and-empty-aggregate", false };
            yield return new object[] { "customer-and-cancel", true };
            yield return new object[] { "customer-and-sensitive-cancel", true };
            yield return new object[] { "customer-and-operation-canceled", true };
            yield return new object[] { "customer-and-wrapped-cancellation", true };
            yield return new object[] { "mixed-and-wrapped-cancellation", false };
            yield return new object[] { "wrapped-cancellation-only", false };
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
        /// Gets failures at each orchestration boundary.
        /// </summary>
        public static IEnumerable<object[]> OrchestratorFailureCases()
        {
            foreach (var testCase in ClassificationCases())
            {
                foreach (var stage in new[] { "discovery", "search", "enqueue" })
                {
                    yield return new[] { testCase[0], testCase[1], stage };
                }
            }
        }

        /// <summary>
        /// Gets failures and independently specified HTTP statuses for job error responses.
        /// </summary>
        public static IEnumerable<object[]> FailureStatusCases()
        {
            yield return new object[] { "cmk", HttpStatusCode.Forbidden };
            yield return new object[] { "wrapped-cmk", HttpStatusCode.Forbidden };
            yield return new object[] { "customer-and-wrapped-cancellation", HttpStatusCode.Forbidden };
            yield return new object[] { "parameters", HttpStatusCode.BadRequest };
            yield return new object[] { "wrapped-parameters", HttpStatusCode.BadRequest };
            yield return new object[] { "wrapped-conflict", HttpStatusCode.Conflict };
            yield return new object[] { "unknown-wrapper-conflict", HttpStatusCode.InternalServerError };
            yield return new object[] { "unknown-wrapper-cancellation", HttpStatusCode.InternalServerError };
            yield return new object[] { "customer-and-empty-aggregate", HttpStatusCode.InternalServerError };
            yield return new object[] { "customer-and-sensitive-cancel", HttpStatusCode.Forbidden };
            yield return new object[] { "timeout", HttpStatusCode.InternalServerError };
            yield return new object[] { "mixed", HttpStatusCode.InternalServerError };
            yield return new object[] { "mixed-and-wrapped-cancellation", HttpStatusCode.InternalServerError };
        }

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
                "unknown-wrapper-conflict" => new InvalidOperationException("Unknown failure.", CreateException("conflict")),
                "unknown-wrapper-cancellation" => new InvalidOperationException("Unknown failure.", new OperationCanceledException()),
                "customer-and-empty-aggregate" => new AggregateException(CreateException("conflict"), new AggregateException()),
                "customer-and-cancel" => Wrap(new AggregateException(CreateException("cmk"), new TaskCanceledException())),
                "customer-and-sensitive-cancel" => Wrap(new AggregateException(CreateException("cmk"), new OperationCanceledException("Patient/secret-123"))),
                "customer-and-operation-canceled" => Wrap(new AggregateException(CreateException("cmk"), new OperationCanceledException())),
                "customer-and-wrapped-cancellation" => Wrap(new AggregateException(
                    new IncompleteOperationException<Dictionary<string, long>>(CreateException("cmk"), new Dictionary<string, long>()),
                    new IncompleteOperationException<Dictionary<string, long>>(new OperationCanceledException(new CancellationToken(true)), new Dictionary<string, long>()))),
                "mixed-and-wrapped-cancellation" => Wrap(new AggregateException(CreateException("customer-and-wrapped-cancellation"), CreateException("timeout"))),
                "wrapped-cancellation-only" => Wrap(new IncompleteOperationException<Dictionary<string, long>>(new OperationCanceledException(new CancellationToken(true)), new Dictionary<string, long>())),
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
