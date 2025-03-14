// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ExceptionExtensions
    {
        private const string _completedSqlTransactionError = "This SqlTransaction has completed; it is no longer usable.";

        /// <summary>
        /// Determines whether the exception or its inner exception is of type <see cref="RequestRateExceededException"/>.
        /// It could be the inner exception because the Cosmos DB SDK wraps exceptions that are thrown inside of custom request handlers with a CosmosException.
        /// </summary>
        /// <param name="e">The exception</param>
        public static bool IsRequestRateExceeded(this Exception e)
        {
            return e.AsRequestRateExceeded() != null;
        }

        /// <summary>
        /// Attempts to return the given exception or its inner exception to a <see cref="RequestRateExceededException"/>.
        /// </summary>
        /// <param name="e">The exception</param>
        public static RequestRateExceededException AsRequestRateExceeded(this Exception e)
        {
            return e as RequestRateExceededException ?? e?.InnerException as RequestRateExceededException;
        }

        /// <summary>
        /// Determines whether the exception or its inner exception is of type <see cref="RequestEntityTooLargeException"/>.
        /// It could be the inner exception because the Cosmos DB SDK wraps exceptions that are thrown inside of custom request handlers with a CosmosException.
        /// </summary>
        /// <param name="e">The exception</param>
        public static bool IsRequestEntityTooLarge(this Exception e)
        {
            return e is RequestEntityTooLargeException || e?.InnerException is RequestEntityTooLargeException;
        }

        /// <summary>
        /// Determines whether the exception if caused by a C# SQL Transaction already committed.
        /// </summary>
        /// <param name="e">The exception</param>
        public static bool IsCompletedTransactionException(this InvalidOperationException e)
        {
            return e?.Message?.Contains(_completedSqlTransactionError, StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
