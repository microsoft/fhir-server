// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            var leaves = GetLeaves(exception).ToList();
            return leaves.Count > 0 && leaves.All(IsCustomerCausedLeaf);
        }

        internal static IEnumerable<Exception> GetLeaves(Exception exception)
        {
            // Typed customer errors retain their classification even when they wrap a raw SQL error.
            if (IsCustomerCausedLeaf(exception) || exception is OperationCanceledException)
            {
                yield return exception;
            }
            else if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Where(inner => inner is not TaskCanceledException))
                {
                    foreach (var leaf in GetLeaves(inner))
                    {
                        yield return leaf;
                    }
                }
            }
            else if (exception.InnerException != null)
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
