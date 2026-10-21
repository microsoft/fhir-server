// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Verifies caller ownership of scopes returned by registered factories.
    /// </summary>
    public static class ScopedFactoryAssert
    {
        /// <summary>
        /// Verifies that a live root provider does not retain a disposed factory-created wrapper.
        /// </summary>
        /// <typeparam name="T">The scoped payload type.</typeparam>
        /// <param name="root">The root provider containing the real factory registration.</param>
        /// <param name="caseName">The service name included in a retention failure.</param>
        public static void NotRetainedByRoot<T>(IServiceProvider root, string caseName)
        {
            Func<IScoped<T>> factory = root.GetRequiredService<Func<IScoped<T>>>();
            WeakReference reference = CreateDisposedScope(factory);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(reference.IsAlive, $"Disposed scoped wrapper for {caseName} is retained by the root provider.");
            GC.KeepAlive(factory);
            GC.KeepAlive(root);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateDisposedScope<T>(Func<IScoped<T>> factory)
        {
            using IScoped<T> scope = factory();
            Assert.NotNull(scope.Value);
            return new WeakReference(scope);
        }
    }
}
