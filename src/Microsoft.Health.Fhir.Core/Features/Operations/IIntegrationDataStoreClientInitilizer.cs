// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IIntegrationDataStoreClientInitilizer<T>
    {
        Task<T> GetAuthorizedClientAsync(CancellationToken cancellationToken);

        Task<T> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration, CancellationToken cancellationToken);
    }
}
