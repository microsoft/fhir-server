// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    ///  Provides a contract that enables extensible IoC configuration for the application
    /// </summary>
    public interface IStartupModule
    {
        /// <summary>
        /// Loads IoC configuration
        /// </summary>
        /// <param name="services">The collection.</param>
        void Load(IServiceCollection services);
    }
}
