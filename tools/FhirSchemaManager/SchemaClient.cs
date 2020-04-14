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
    internal class SchemaClient : ISchemaClient
    {
        private const string CurrentUrl = "/_schema/versions/current";
        private static HttpClient _httpClient;

        public SchemaClient(Uri serverUri)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = serverUri;
        }

        public async Task<List<CurrentVersion>> GetCurrentVersionInformation()
        {
            var response = await _httpClient.GetAsync(new Uri(CurrentUrl, UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }
            else
            {
                throw new SchemaManagerException(Resources.CurrentDefaultErrorDescription);
            }
        }
    }
}
