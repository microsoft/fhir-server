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
