// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public sealed class DocumentClientScope : IScoped<IDocumentClient>
    {
        private Action _dispose;

        public DocumentClientScope(IDocumentClient instance, Action dispose)
            : this(instance)
        {
            EnsureArg.IsNotNull(dispose, nameof(dispose));

            _dispose = dispose;
        }

        public DocumentClientScope(IDocumentClient instance)
        {
            EnsureArg.IsNotNull(instance, nameof(instance));

            Value = instance;
        }

        public IDocumentClient Value { get; }

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
