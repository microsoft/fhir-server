// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    /// <summary>
    /// Implement basic auth logic using ACR admin keys.
    /// Reference: https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#catalog-listing-using-spadmin-with-basic-auth
    /// </summary>
    public class ContainerRegistryBasicTokenProvider : IContainerRegistryTokenProvider
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> GetTokenAsync(ContainerRegistryInfo containerRegistryInfo, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            return GenerateBasicToken(containerRegistryInfo.ContainerRegistryUsername, containerRegistryInfo.ContainerRegistryPassword);
        }

        private static string GenerateBasicToken(string username, string password)
        {
            var input = $"{username}:{password}";
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var base64 = Convert.ToBase64String(inputBytes);
            return $"Basic {base64}";
        }
    }
}
