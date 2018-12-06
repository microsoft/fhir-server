// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class SearchFhirClient : FhirClient
    {
        private const string TagSearchParameterName = "_tag";

        public SearchFhirClient(HttpClient httpClient, ResourceFormat format, string testSessionId)
            : base(httpClient, format)
        {
            TestSessionId = testSessionId;
        }

        public string TestSessionId { get; set; }

        private string TagQueryString => $"{TagSearchParameterName}={TestSessionId}";

        public override Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource)
        {
            (resource.Meta = resource.Meta ?? new Meta()).Tag.Add(new Coding(null, TestSessionId));

            return base.CreateAsync(uri, resource);
        }

        public override Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null)
        {
            (resource.Meta = resource.Meta ?? new Meta()).Tag.Add(new Coding(null, TestSessionId));

            return base.UpdateAsync(uri, resource, ifMatchVersion);
        }

        public override Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null)
        {
            query = query == null ?
                TagQueryString :
                $"{TagQueryString}&{query}";

            return base.SearchAsync(resourceType, query, count);
        }

        public override Task<FhirResponse<Bundle>> SearchAsync(string url)
        {
            if (!url.Contains(TagQueryString))
            {
                char separator = url.Contains("?") ? '&' : '?';

                url = $"{url}{separator}{TagQueryString}";
            }

            return base.SearchAsync(url);
        }

        public override Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, params (string key, string value)[] body)
        {
            IEnumerable<(string, string)> entries = body;

            if (!body.Contains((TagSearchParameterName, TestSessionId)))
            {
                entries = entries.Append((TagSearchParameterName, TestSessionId));
            }

            return base.SearchPostAsync(resourceType, entries.ToArray());
        }
    }
}
