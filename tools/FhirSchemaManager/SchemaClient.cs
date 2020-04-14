// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FhirSchemaManager.Exceptions;
using FhirSchemaManager.Model;
using FhirSchemaManager.Utils;
using Newtonsoft.Json;

namespace FhirSchemaManager
{
    internal class SchemaClient : ISchemaClient
    {
        private static HttpClient _httpClient;

        public SchemaClient(Uri serverUri)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = serverUri;
        }

        public async Task<List<CurrentVersion>> GetCurrentVersionInformation()
        {
            var response = await _httpClient.GetAsync(RelativeUrl(UrlConstants.Current));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }
            else
            {
                throw new SchemaManagerException(string.Format(Resources.CurrentDefaultErrorDescription, response.StatusCode));
            }
        }

        public async Task<string> GetScript(Uri scriptUri)
        {
            var response = await _httpClient.GetAsync(scriptUri);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                throw new SchemaManagerException(string.Format(Resources.ScriptNotFound, response.StatusCode));
            }
        }

        public async Task<CompatibleVersion> GetCompatibility()
        {
            var response = await _httpClient.GetAsync(RelativeUrl(UrlConstants.Compatibility));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<CompatibleVersion>(responseBodyAsString);
            }
            else
            {
                throw new SchemaManagerException(string.Format(Resources.CompatibilityDefaultErrorMessage, response.StatusCode));
            }
        }

        public async Task<List<AvailableVersion>> GetAvailability()
        {
            var response = await _httpClient.GetAsync(RelativeUrl(UrlConstants.Availability));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<AvailableVersion>>(responseBodyAsString);
            }
            else
            {
                throw new SchemaManagerException(string.Format(Resources.AvailableVersionsDefaultErrorMessage, response.StatusCode));
            }
        }

        private Uri RelativeUrl(string url)
        {
            return new Uri(url, UriKind.Relative);
        }
    }
}
