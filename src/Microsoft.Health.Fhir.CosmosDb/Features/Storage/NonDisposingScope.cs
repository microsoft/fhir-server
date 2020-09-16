// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class NonDisposingScope : IScoped<Container>
    {
        public NonDisposingScope(Container container)
        {
            EnsureArg.IsNotNull(container, nameof(container));
            Value = container;
        }

        public Container Value { get; }

        public void Dispose()
        {
        }
    }
}
