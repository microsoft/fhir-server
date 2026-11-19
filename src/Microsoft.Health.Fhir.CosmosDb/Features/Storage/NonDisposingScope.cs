// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A non-owning handle that leaves the shared container's lifetime with its owner.
    /// </summary>
    public sealed class NonDisposingScope : IScoped<Container>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonDisposingScope"/> class.
        /// </summary>
        /// <param name="container">The shared container, which this handle does not dispose.</param>
        public NonDisposingScope(Container container)
        {
            EnsureArg.IsNotNull(container, nameof(container));
            Value = container;
        }

        /// <summary>
        /// Gets the shared container, including after this handle is disposed.
        /// </summary>
        public Container Value { get; }

        /// <summary>
        /// Does nothing; the shared container remains owned by its original owner.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
