// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Scoping;
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
            IOwnedScopeFactory<T> factory = root.GetRequiredService<IOwnedScopeFactory<T>>();
            WeakReference reference = CreateDisposedScope(factory);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(reference.IsAlive, $"Disposed scoped wrapper for {caseName} is retained by the root provider. Expected IsAlive: False; actual: {reference.IsAlive}.");
            GC.KeepAlive(factory);
            GC.KeepAlive(root);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateDisposedScope<T>(IOwnedScopeFactory<T> factory)
        {
            using IOwnedScope<T> scope = factory.Create();
            Assert.NotNull(scope.Value);
            return new WeakReference(scope);
        }
    }
}
