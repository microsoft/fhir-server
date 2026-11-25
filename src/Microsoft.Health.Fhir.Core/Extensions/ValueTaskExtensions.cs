// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class ValueTaskExtensions
    {
        /// <summary>
        /// Gets the result of a task that ran with only synchronous calls (the Azure SDK <c>async: false</c> pattern).
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="task">A task returned by a load that used only synchronous calls.</param>
        /// <returns>The task's result.</returns>
        /// <exception cref="InvalidOperationException">The task has not completed, so the caller would block.</exception>
        public static T EnsureCompleted<T>(this ValueTask<T> task)
        {
            if (!task.IsCompleted)
            {
                throw new InvalidOperationException("A synchronous load awaited an incomplete task. Loaders must not await when async is false.");
            }

            return task.GetAwaiter().GetResult();
        }
    }
}
