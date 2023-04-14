// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    /// <summary>
    /// An implementation of IContainerRegistryTokenProvider that provides null rather than an access token
    /// </summary>
    public class DefaultTokenProvider : IContainerRegistryTokenProvider
    {
        private readonly ILogger<DefaultTokenProvider> _logger;

        public DefaultTokenProvider(ILogger<DefaultTokenProvider> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        public Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken) // TODO: SHAUN - will this method being syncronous cause an issue?
        {
            _logger.LogInformation("Accessing default token provider");

            return null;
        }
    }
}
