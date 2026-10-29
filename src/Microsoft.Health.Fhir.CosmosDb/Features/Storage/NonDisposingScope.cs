// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A handle that leaves the shared container's lifetime with its owner.
    /// </summary>
    public sealed class NonDisposingScope : IScoped<Container>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonDisposingScope"/> class.
        /// </summary>
        /// <param name="container">The shared container.</param>
        public NonDisposingScope(Container container)
        {
            EnsureArg.IsNotNull(container, nameof(container));
            Value = container;
        }

        /// <inheritdoc />
        public Container Value { get; }

        /// <summary>
        /// Leaves the shared container undisposed.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
