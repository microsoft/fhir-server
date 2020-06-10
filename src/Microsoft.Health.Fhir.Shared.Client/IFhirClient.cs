// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Client
{
    public interface IFhirClient
    {
        Uri AuthorizeUri { get; }

        ResourceFormat Format { get; }

        HttpClient HttpClient { get; }

        bool? SecurityEnabled { get; }

        DateTime TokenExpiration { get; }

        Uri TokenUri { get; }

        Task<HttpResponseMessage> CheckExportAsync(Uri contentLocation, CancellationToken cancellationToken = default);

        Task<FhirResponse<T>> ConditionalUpdateAsync<T>(T resource, string searchCriteria, string ifMatchVersion = null, CancellationToken cancellationToken = default)
            where T : Resource;

        Task ConfigureSecurityOptions(CancellationToken cancellationToken = default);

        Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource, string conditionalCreateCriteria = null, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse<T>> CreateAsync<T>(T resource, string conditionalCreateCriteria = null, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse> DeleteAsync(string uri, CancellationToken cancellationToken = default);

        Task<FhirResponse> DeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<Uri> ExportAsync(string path = "", CancellationToken cancellationToken = default);

        Task<FhirResponse> HardDeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse> PatchAsync(string uri, string content, CancellationToken cancellationToken = default);

        Task<FhirResponse<Bundle>> PostBundleAsync(Resource bundle, CancellationToken cancellationToken = default);

        Task<FhirResponse<T>> ReadAsync<T>(ResourceType resourceType, string resourceId, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse<T>> ReadAsync<T>(string uri, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null, CancellationToken cancellationToken = default);

        Task<FhirResponse<Bundle>> SearchAsync(string url, CancellationToken cancellationToken = default);

        Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, CancellationToken cancellationToken = default, params (string key, string value)[] body);

        void SetBearerToken(string token);

        Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchVersion = null, CancellationToken cancellationToken = default)
            where T : Resource;

        Task<OperationOutcome> ValidateAsync(string uri, string resource, bool xml = false, CancellationToken cancellationToken = default);

        Task<FhirResponse<T>> VReadAsync<T>(ResourceType resourceType, string resourceId, string versionId, CancellationToken cancellationToken = default)
            where T : Resource;
    }
}
