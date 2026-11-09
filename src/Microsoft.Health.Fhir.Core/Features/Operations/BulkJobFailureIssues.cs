// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    internal static class BulkJobFailureIssues
    {
        private const string Cancellation = "A task was canceled.";
        private const string ConflictPrefix = "JobConflictException:";

        internal static string FromException(Exception exception)
        {
            if (CustomerCausedExceptionExtensions.GetLeaves(exception).All(leaf => leaf is OperationCanceledException))
            {
                return Cancellation;
            }

            var message = exception.GetJobFailureMessage();
            return exception.GetJobFailureStatusCode() == HttpStatusCode.Conflict
                ? $"{ConflictPrefix} {message}"
                : message;
        }

        internal static bool IsCancellation(string issue) => issue == Cancellation;

        internal static bool IsConflict(string issue) => issue.StartsWith(ConflictPrefix, StringComparison.Ordinal);
    }
}
