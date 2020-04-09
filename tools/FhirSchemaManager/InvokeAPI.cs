// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FhirSchemaManager.Exceptions;
using FhirSchemaManager.Model;
using Newtonsoft.Json;

namespace FhirSchemaManager
{
    internal class InvokeAPI : IInvokeAPI
    {
        private const string CurrentUrl = "/_schema/versions/current";

        private static HttpClient _httpClient;

        public InvokeAPI()
        {
            _httpClient = new HttpClient();
        }

        public async Task<List<CurrentVersion>> GetCurrentVersionInformation(Uri serverUri)
        {
            _httpClient.BaseAddress = serverUri;

            var response = await _httpClient.GetAsync(new Uri(CurrentUrl, UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }
            else
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        throw new SchemaOperationFailedException(response.StatusCode, Resources.CurrentInformationNotFound);

                    default:
                        throw new SchemaOperationFailedException(response.StatusCode, Resources.CurrentDefaultErrorDescription);
                }
            }
        }
    }
}
