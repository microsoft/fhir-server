// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Implementations of this interface registered in the IoC container will have their <see cref="Start"/> method
    /// called during application startup, before the IWebHost is created. This is intended for instances that require
    /// eager initialization.
    /// </summary>
    public interface IStartable
    {
        /// <summary>
        /// Called during application startup.
        /// </summary>
        void Start();
    }
}
