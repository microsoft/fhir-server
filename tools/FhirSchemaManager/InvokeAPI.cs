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
using Newtonsoft.Json;

namespace FhirSchemaManager
{
    internal class InvokeAPI : IInvokeAPI
    {
        public async Task<List<CurrentVersion>> GetCurrentVersionInformation(Uri serverUri)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = serverUri,
            };

            var response = await httpClient.GetAsync(new Uri("/_schema/versions/current", UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }
            else
            {
                throw new SchemaOperationFailedException(string.Format(Resources.CurrentDefaultErrorDescription, response.StatusCode));
            }
        }

        public async Task<string> GetScript(Uri scriptUri)
        {
            var httpClient = new HttpClient();

            var response = await httpClient.GetAsync(scriptUri);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                throw new SchemaOperationFailedException(string.Format(Resources.ScriptNotFound, response.StatusCode));
            }
        }

        public async Task<CompatibleVersion> GetCompatibility(Uri serverUri)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = serverUri,
            };

            var response = await httpClient.GetAsync(new Uri("/_schema/compatibility", UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<CompatibleVersion>(responseBodyAsString);
            }
            else
            {
                throw new SchemaOperationFailedException(string.Format(Resources.CompatibilityDefaultErrorMessage, response.StatusCode));
            }
        }

        public async Task<List<AvailableVersion>> GetAvailability(Uri serverUri)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = serverUri,
            };

            var response = await httpClient.GetAsync(new Uri("/_schema/versions", UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<AvailableVersion>>(responseBodyAsString);
            }
            else
            {
                throw new SchemaOperationFailedException(string.Format(Resources.AvailableVersionsDefaultErrorMessage, response.StatusCode));
            }
        }
    }
}
