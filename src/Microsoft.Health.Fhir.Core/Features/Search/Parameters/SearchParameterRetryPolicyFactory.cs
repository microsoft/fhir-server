// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Polly;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    /// <summary>
    /// Static factory for executing search parameter operations with retry logic.
    /// Encapsulates the logic for determining when to apply retry based on context state.
    /// </summary>
    public static class SearchParameterRetryPolicyFactory
    {
        private const int MaxRetryCount = 3;

        /// <summary>
        /// Executes the provided function with retry logic if appropriate.
       /// </summary>
        /// <typeparam name="TResult">The type of result returned by the action.</typeparam>
        /// <param name="requestContextAccessor">The request context accessor to check execution context.</param>
        /// <param name="action">The action to execute with optional retry.</param>
        /// <param name="onRetry">Optional callback invoked on each retry for custom logging or actions.</param>
        public static async Task<TResult> ExecuteAsync<TResult>(RequestContextAccessor<IFhirRequestContext> requestContextAccessor, Func<Task<TResult>> action, Action<Exception, TimeSpan, int> onRetry = null)
        {
            var isParallelBundle = requestContextAccessor.RequestContext.Properties.TryGetValue("BundleProcessingLogic", out var value) && value?.ToString() == "Parallel";

            if (isParallelBundle)
            {
                return await action();
            }

            // Apply retry policy for individual requests and sequential bundle entries
            var retryPolicy = Policy
                .Handle<BadRequestException>(ex => ex.Message == Core.Resources.SearchParameterConcurrencyConflict)
                .WaitAndRetryAsync(
                    retryCount: MaxRetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(1),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        // Clear the lastUpdated before retrying
                        requestContextAccessor.RequestContext?.ClearSearchParameterLastUpdated();

                        // Allow caller to provide custom logging or actions
                        onRetry?.Invoke(exception, timeSpan, retryCount);
                    });

            try
            {
                return await retryPolicy.ExecuteAsync(action);
            }
            catch (BadRequestException ex) when (ex.Message == Core.Resources.SearchParameterConcurrencyConflict)
            {
                // All retries exhausted - enhance the exception message
                throw new BadRequestException($"{ex.Message} Retries={MaxRetryCount}");
            }
        }

        /// <summary>
        /// Convenience overload for actions with no return value.
        /// </summary>
        public static async Task ExecuteAsync(RequestContextAccessor<IFhirRequestContext> requestContextAccessor, Func<Task> action, Action<Exception, TimeSpan, int> onRetry = null)
        {
            // Delegate to the generic version with a dummy return value
            await ExecuteAsync(
                requestContextAccessor,
                async () =>
                {
                    await action();
                    return 0; // dummy value, discarded
                },
                onRetry);
        }
    }
}
