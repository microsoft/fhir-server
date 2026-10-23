// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Runs test operations as a server-owned background task, matching how export and bulk update jobs call search.
    /// </summary>
    internal static class BackgroundTaskRequestContext
    {
        /// <summary>
        /// Executes <paramref name="operation"/> with a request context marked as a background task, then restores the original context.
        /// </summary>
        /// <typeparam name="T">The operation result type.</typeparam>
        /// <param name="contextAccessor">The request context accessor used by the code under test.</param>
        /// <param name="operation">The operation to run.</param>
        /// <returns>The operation result.</returns>
        public static async Task<T> RunAsync<T>(RequestContextAccessor<IFhirRequestContext> contextAccessor, Func<Task<T>> operation)
        {
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(operation, nameof(operation));

            IFhirRequestContext original = contextAccessor.RequestContext;
            IFhirRequestContext backgroundContext = (IFhirRequestContext)original?.Clone() ?? new DefaultFhirRequestContext();
            backgroundContext.IsBackgroundTask = true;
            contextAccessor.RequestContext = backgroundContext;

            try
            {
                return await operation();
            }
            finally
            {
                contextAccessor.RequestContext = original;
            }
        }
    }
}
