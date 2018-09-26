// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Represents a scoped usage of a component.
    /// </summary>
    /// <typeparam name="T">Component type to resolve</typeparam>
    public interface IScoped<T> : IDisposable
    {
        T Value { get; }
    }
}
