// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.CosmosDb.Features.Storage.Versioning
{
    public interface ICollectionUpdater
    {
        Task ExecuteAsync(IDocumentClient client, DocumentCollection collection, Uri relativeCollectionUri);
    }
}
