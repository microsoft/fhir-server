// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class CustomerCausedExceptionExtensions
    {
        /// <summary>
        /// Returns true only when every underlying failure is a recognized customer error.
        /// </summary>
        public static bool IsCustomerCaused(this Exception exception)
        {
            return exception.GetJobFailureStatusCode() != HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Maps recognized customer failures to their API status, keeping mixed or unknown failures internal errors.
        /// </summary>
        public static HttpStatusCode GetJobFailureStatusCode(this Exception exception)
        {
            // Page failures cancel siblings, whose cancellations can be nested inside incomplete-operation wrappers.
            var leaves = GetLeaves(exception).Where(leaf => leaf is not OperationCanceledException).ToList();
            if (leaves.Count == 0 || !leaves.All(IsCustomerCausedLeaf))
            {
                return HttpStatusCode.InternalServerError;
            }

            if (leaves.Any(leaf => leaf is CustomerManagedKeyException))
            {
                return HttpStatusCode.Forbidden;
            }

            return leaves.Any(leaf => leaf is Features.Operations.JobConflictException)
                ? HttpStatusCode.Conflict
                : HttpStatusCode.BadRequest;
        }

        /// <summary>
        /// Returns a client-safe description of a classified job failure.
        /// </summary>
        public static string GetJobFailureMessage(this Exception exception)
        {
            return exception.GetJobFailureStatusCode() switch
            {
                HttpStatusCode.Forbidden => Resources.OperationFailedForCustomerManagedKey,
                HttpStatusCode.Conflict => Resources.ReindexRunningException,
                HttpStatusCode.BadRequest => Resources.TooManyParameters,
                _ => Resources.InternalServerError,
            };
        }

        internal static string GetBulkJobFailureIssue(this Exception exception)
        {
            if (GetLeaves(exception).All(leaf => leaf is OperationCanceledException))
            {
                return "A task was canceled.";
            }

            var message = exception.GetJobFailureMessage();
            return exception.GetJobFailureStatusCode() == HttpStatusCode.Conflict
                ? $"JobConflictException: {message}"
                : message;
        }

        internal static IEnumerable<Exception> GetLeaves(Exception exception)
        {
            // Typed customer errors retain their classification even when they wrap a raw SQL error.
            if (IsCustomerCausedLeaf(exception) || exception is OperationCanceledException)
            {
                yield return exception;
            }
            else if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    foreach (var leaf in GetLeaves(inner))
                    {
                        yield return leaf;
                    }
                }
            }
            else if (exception.GetType().IsGenericType
                && exception.GetType().GetGenericTypeDefinition() == typeof(IncompleteOperationException<>)
                && exception.InnerException != null)
            {
                foreach (var leaf in GetLeaves(exception.InnerException))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return exception;
            }
        }

        private static bool IsCustomerCausedLeaf(Exception exception) =>
            exception is CustomerManagedKeyException
            || exception is Features.Operations.JobConflictException
            || (exception is RequestNotValidException invalidRequest && invalidRequest.Message == Resources.TooManyParameters);
    }
}
