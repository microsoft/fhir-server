// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private static Func<IFhirPathProvider> _factory = static () => new FirelyFhirPathProvider();
        private static Lazy<IFhirPathProvider> _instance = new(() => _factory());

        /// <summary>
        /// Gets the configured provider.
        /// </summary>
        public static IFhirPathProvider Instance => _instance.Value;

        /// <summary>
        /// Replaces the provider factory. The provider is created lazily and exactly once.
        /// </summary>
        /// <param name="factory">The provider factory.</param>
        public static void SetProviderFactory(Func<IFhirPathProvider> factory)
        {
            _factory = EnsureArg.IsNotNull(factory, nameof(factory));
            _instance = new Lazy<IFhirPathProvider>(() => _factory());
        }
    }
}
