﻿// -------------------------------------------------------------------------------------------------
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
        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A client of type T</returns>
        Task<T> GetAuthorizedClientAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Used to get a client that is authorized to talk to the integration data store.
        /// </summary>
        /// <param name="integrationDataStoreConfiguration">Integration dataStore configuration</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A client of type T</returns>
        Task<T> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration, CancellationToken cancellationToken);
    }
}
