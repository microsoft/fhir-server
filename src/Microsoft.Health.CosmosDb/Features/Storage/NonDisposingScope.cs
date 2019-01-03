// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public sealed class NonDisposingScope : IScoped<IDocumentClient>
    {
        public NonDisposingScope(IDocumentClient documentClient)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            Value = documentClient;
        }

        public IDocumentClient Value { get; }

        public void Dispose()
        {
        }
    }
}
