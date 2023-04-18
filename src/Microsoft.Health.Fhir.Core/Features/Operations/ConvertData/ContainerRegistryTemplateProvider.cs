// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class ContainerRegistryTemplateProvider : DefaultTemplateProvider, IConvertDataTemplateProvider
    {
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;

        public ContainerRegistryTemplateProvider(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<ConvertDataConfiguration> convertDataConfig,
            ILogger<ContainerRegistryTemplateProvider> logger)
            : base(convertDataConfig, logger)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider, nameof(containerRegistryTokenProvider));

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
        }

        /// <summary>
        /// Fetch template collection from container registry or built-in archive.
        /// </summary>
        /// <param name="request">The convert data request which contains template reference.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the fetch operation.</param>
        /// <returns>Template collection.</returns>
        public override async Task<List<Dictionary<string, Template>>> GetTemplateCollectionAsync(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            // We have embedded a default template collection in the templatemanagement package.
            // If the template collection is the default reference, we don't need to retrieve token.
            var accessToken = string.Empty;
            if (!request.IsDefaultTemplateReference)
            {
                Logger.LogInformation("Using a custom template collection for data conversion.");

                async Task<string> TokenEntryFactory(ICacheEntry entry)
                {
                    var token = await _containerRegistryTokenProvider.GetTokenAsync(request.RegistryServer, cancellationToken);
                    entry.Size = token.Length;
                    entry.AbsoluteExpiration = GetTokenAbsoluteExpiration(token);
                    return token;
                }

                accessToken = await Cache.GetOrCreateAsync(GetCacheKey(request.RegistryServer), TokenEntryFactory);
            }
            else
            {
                Logger.LogInformation("Using the default template collection for data conversion.");
            }

            return await GetTemplatesFromRequestAsync(request, accessToken, cancellationToken);
        }

        /// <summary>
        /// Try to parse exp claim from the acr JWT token. Return 30 minutes as default expiration.
        /// </summary>
        /// <param name="accessToken">JWT token with "Bearer" prefix.</param>
        /// <returns>Expiration DateTimeOffset.</returns>
        private static DateTimeOffset GetTokenAbsoluteExpiration(string accessToken)
        {
            var defaultExpiration = DateTimeOffset.Now.AddMinutes(30);
            if (accessToken.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var jwtTokenText = accessToken.Substring(7);
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadToken(jwtTokenText) as JwtSecurityToken;

                // Add 5 minutes buffer in case of last minute expirations.
                return new DateTimeOffset(jwtToken.ValidTo).AddMinutes(-5);
            }

            return defaultExpiration;
        }
    }
}
