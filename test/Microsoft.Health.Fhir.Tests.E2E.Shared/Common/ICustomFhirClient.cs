// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public interface ICustomFhirClient
    {
        ResourceFormat Format { get; }

        (bool SecurityEnabled, string AuthorizeUrl, string TokenUrl) SecuritySettings { get; }

        HttpClient HttpClient { get; }

        Task RunAsUser(TestUser user, TestApplication clientApplication);

        Task RunAsClientApplication(TestApplication clientApplication);

        Task<FhirResponse<T>> CreateAsync<T>(T resource)
            where T : Resource;

        Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource)
            where T : Resource;

        Task<FhirResponse<T>> ReadAsync<T>(ResourceType resourceType, string resourceId)
            where T : Resource;

        Task<FhirResponse<T>> ReadAsync<T>(string uri)
            where T : Resource;

        Task<FhirResponse<T>> VReadAsync<T>(ResourceType resourceType, string resourceId, string versionId)
            where T : Resource;

        Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchVersion = null)
            where T : Resource;

        Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null)
            where T : Resource;

        Task<FhirResponse> DeleteAsync<T>(T resource)
            where T : Resource;

        Task<FhirResponse> DeleteAsync(string uri);

        Task<FhirResponse> HardDeleteAsync<T>(T resource)
            where T : Resource;

        Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null);

        Task<FhirResponse<Bundle>> SearchAsync(string url);

        Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, params (string key, string value)[] body);

        Task<string> ExportAsync(Dictionary<string, string> queryParams);

        StringContent CreateStringContent(Resource resource);

        Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response);

        Task<FhirResponse<T>> CreateResponseAsync<T>(HttpResponseMessage response)
            where T : Resource;

        Task SetupAuthenticationAsync(TestApplication clientApplication, TestUser user = null);

        Task<string> GetBearerToken(TestApplication clientApplication, TestUser user);

        List<KeyValuePair<string, string>> GetAppSecuritySettings(TestApplication clientApplication);

        List<KeyValuePair<string, string>> GetUserSecuritySettings(TestApplication clientApplication, TestUser user);

        Task GetSecuritySettings(string fhirServerMetadataUrl);
    }
}
