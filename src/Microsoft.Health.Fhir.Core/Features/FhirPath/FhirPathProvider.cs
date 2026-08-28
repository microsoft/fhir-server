// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Provides process-wide access to the configured FHIRPath engine.
    /// </summary>
    /// <remarks>
    /// This ambient is required by static extension call sites. In-process servers configured with
    /// different providers are not supported; DI consumers should inject <see cref="IFhirPathProvider"/>.
    /// </remarks>
    public static class FhirPathProvider
    {
        private static Lazy<IFhirPathProvider> _instance = CreateLazy(static () => new FirelyFhirPathProvider());

        /// <summary>
        /// Gets the configured provider.
        /// </summary>
        public static IFhirPathProvider Instance => Volatile.Read(ref _instance).Value;

        /// <summary>
        /// Replaces the provider factory. The provider is created lazily and exactly once for each factory.
        /// </summary>
        /// <param name="factory">The provider factory.</param>
        public static void SetProviderFactory(Func<IFhirPathProvider> factory)
        {
            Func<IFhirPathProvider> providerFactory = EnsureArg.IsNotNull(factory, nameof(factory));
            Interlocked.Exchange(ref _instance, CreateLazy(providerFactory));
        }

        private static Lazy<IFhirPathProvider> CreateLazy(Func<IFhirPathProvider> factory)
            => new(
                () => factory() ?? throw new InvalidOperationException("The FHIRPath provider factory returned null."),
                LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
