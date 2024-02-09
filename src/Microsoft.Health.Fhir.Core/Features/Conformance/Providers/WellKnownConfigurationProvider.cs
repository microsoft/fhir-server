// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Providers
{
    public class WellKnownConfigurationProvider : IWellKnownConfigurationProvider
    {
        private const string OpenIdConfigurationPath = ".well-known/openid-configuration";
        private const string SmartConfigurationPath = ".well-known/smart-configuration";

        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WellKnownConfigurationProvider> _logger;

        public WellKnownConfigurationProvider(
            IOptions<SecurityConfiguration> securityConfigurationOptions,
            IHttpClientFactory httpClientFactory,
            ILogger<WellKnownConfigurationProvider> logger)
        {
            _securityConfiguration = EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            _httpClientFactory = EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public bool IsSmartConfigured()
        {
            return !string.IsNullOrWhiteSpace(_securityConfiguration?.SmartAuthentication?.Authority);
        }

        public Task<OpenIdConfigurationResponse> GetOpenIdConfigurationAsync(CancellationToken cancellationToken)
        {
            return GetConfigurationAsync<OpenIdConfigurationResponse>(OpenIdConfigurationPath, cancellationToken);
        }

        public Task<GetSmartConfigurationResponse> GetSmartConfigurationAsync(CancellationToken cancellationToken)
        {
            Uri configurationUrl = GetConfigurationUrl(SmartConfigurationPath);

            if (configurationUrl != null && string.Equals("localhost", configurationUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("The FHIR service is running locally with no Authority set, {URL} is not available.", SmartConfigurationPath);
                return Task.FromResult<GetSmartConfigurationResponse>(null);
            }

            return GetConfigurationAsync<GetSmartConfigurationResponse>(SmartConfigurationPath, cancellationToken);
        }

        private async Task<TConfiguration> GetConfigurationAsync<TConfiguration>(string configurationPath, CancellationToken cancellationToken)
            where TConfiguration : class
        {
            Uri configurationUrl = GetConfigurationUrl(configurationPath);

            if (configurationUrl != null)
            {
                _logger.LogInformation("Fetching configuration using well-known endpoint: {ConfigurationUrl}.", configurationUrl.AbsoluteUri);

                using HttpClient client = _httpClientFactory.CreateClient();
                using var smartConfigurationRequest = new HttpRequestMessage(HttpMethod.Get, configurationUrl);
                HttpResponseMessage response = await client.SendAsync(smartConfigurationRequest, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully fetched the configuration from {ConfigurationUrl}.", configurationUrl.AbsoluteUri);

                    try
                    {
                        string smartConfigurationJson = await response.Content.ReadAsStringAsync(cancellationToken: cancellationToken);
                        return JsonConvert.DeserializeObject<TConfiguration>(smartConfigurationJson);
                    }
                    catch (JsonSerializationException ex)
                    {
                        _logger.LogError(ex, "An error parsing the configuration response from \"{ConfigurationUrl}\" occurred.", configurationUrl.AbsoluteUri);
                        return null;
                    }
                }

                _logger.LogWarning("The configuration request to \"{ConfigurationUrl}\" returned a {StatusCode} status code.", configurationUrl.AbsoluteUri, response.StatusCode);
                return null;
            }

            _logger.LogInformation("Authority is not valid cannot process {ConfigurationPath} request.", configurationPath);

            return null;
        }

        private Uri GetConfigurationUrl(string configurationPath)
        {
            // Prefer the SmartAuthentication authority, but default to Authentication authority.
            string authority = _securityConfiguration?.SmartAuthentication?.Authority ?? _securityConfiguration?.Authentication?.Authority;

            if (!string.IsNullOrWhiteSpace(authority))
            {
                if (!authority.EndsWith('/'))
                {
                    authority += "/";
                }

                try
                {
                    return new Uri($"{authority}{configurationPath}");
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
