// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Polly;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public static class SearchParameterRetry
    {
        private const int MaxRetryCount = 3;

        /// <summary>
        /// Executes the provided function with retry logic.
        /// </summary>
        /// <typeparam name="TResult">The type of result returned by the action.</typeparam>
        /// <param name="action">The action to execute with optional retry.</param>
        /// <param name="info">Additional context information to append to exception messages.</param>
        public static async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, string info = null)
        {
            var retryPolicy = Policy
                .Handle<BadRequestException>(ex => ex.Message == Core.Resources.SearchParameterConcurrencyConflict)
                .WaitAndRetryAsync(
                    retryCount: MaxRetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(1, 5) * 0.1));

            try
            {
                return await retryPolicy.ExecuteAsync(action);
            }
            catch (BadRequestException ex) when (ex.Message == Core.Resources.SearchParameterConcurrencyConflict)
            {
                throw new BadRequestException($"{ex.Message} {info}.{MaxRetryCount}");
            }
        }

        /// <summary>
        /// Convenience overload for actions with no return value.
        /// </summary>
        public static async Task ExecuteAsync(Func<Task> action, string info = null)
        {
            await ExecuteAsync(
                async () =>
                {
                    await action();
                    return 0;
                },
                info);
        }
    }
}
