// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Implementations of this interface registered in the IoC container will have <see cref="EnsureInitialized"/> called on them
    /// from a middleware component before a the first request is handled by any controller. Once the method returns a completed task,
    /// it is no longer guaranteed to be called. It is meant for components that need to be initialized before the web requests can be
    /// handled. Implementations are required to ensure thread safety. This is currently intended only for singleton instances.
    /// </summary>
    public interface IRequireInitializationOnFirstRequest
    {
        /// <summary>
        /// Guaranteed to be called once per request, until it returns a successfully completed task, after which, as an optimization, it might no longer be called.
        /// </summary>
        /// <returns>A task</returns>
        Task EnsureInitialized();
    }
}
