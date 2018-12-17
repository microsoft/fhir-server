// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public interface ICollectionInitializer
    {
        Task<DocumentCollection> InitializeCollection(IDocumentClient documentClient);
    }
}
