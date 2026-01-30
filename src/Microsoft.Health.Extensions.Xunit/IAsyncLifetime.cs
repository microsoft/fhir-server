// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Compatibility shim for xUnit v3's IAsyncLifetime signature changes.
    /// </summary>
    public interface IAsyncLifetime : global::Xunit.IAsyncLifetime
    {
        new Task InitializeAsync();

        new Task DisposeAsync();

        ValueTask global::Xunit.IAsyncLifetime.InitializeAsync() => new ValueTask(InitializeAsync());

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return new ValueTask(DisposeAsync());
        }
    }
}
