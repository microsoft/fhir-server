// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage
{
    public interface ICosmosClientTestProvider
    {
        Task PerformTestAsync(Container container, CancellationToken cancellationToken = default);
    }
}
