// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    /// <summary>
    /// Implement basic auth logic using ACR admin keys.
    /// Reference: https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#catalog-listing-using-spadmin-with-basic-auth
    /// </summary>
    public class ContainerRegistryBasicTokenProvider : IContainerRegistryTokenProvider
    {
        private readonly DataConvertConfiguration _dataConvertConfiguration;

        public ContainerRegistryBasicTokenProvider(IOptions<DataConvertConfiguration> dataConvertConfiguration)
        {
            EnsureArg.IsNotNull(dataConvertConfiguration, nameof(dataConvertConfiguration));

            _dataConvertConfiguration = dataConvertConfiguration.Value;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var containerRegistryInfo = _dataConvertConfiguration.ContainerRegistries
                .FirstOrDefault(registry => string.Equals(registry.ContainerRegistryServer, registryServer, StringComparison.OrdinalIgnoreCase));
            if (containerRegistryInfo == null)
            {
                throw new ContainerRegistryNotRegisteredException(string.Format(Resources.ContainerRegistryNotRegistered, registryServer));
            }

            if (string.IsNullOrEmpty(containerRegistryInfo.ContainerRegistryServer)
                || string.IsNullOrEmpty(containerRegistryInfo.ContainerRegistryUsername)
                || string.IsNullOrEmpty(containerRegistryInfo.ContainerRegistryPassword))
            {
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, containerRegistryInfo.ContainerRegistryServer));
            }

            return string.Format("Basic {0}", GenerateBasicToken(containerRegistryInfo.ContainerRegistryUsername, containerRegistryInfo.ContainerRegistryPassword));
        }

        private static string GenerateBasicToken(string username, string password)
        {
            var input = $"{username}:{password}";
            var inputBytes = Encoding.UTF8.GetBytes(input);
            return Convert.ToBase64String(inputBytes);
        }
    }
}
