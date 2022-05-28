// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceRequest
    {
        [JsonConstructor]
        protected ResourceRequest()
        {
        }

        public ResourceRequest(string method, Uri url = null)
        {
            EnsureArg.IsNotNullOrEmpty(method, nameof(method));

            Url = url != null ? null : null;
            Method = method;
        }

        public ResourceRequest(HttpMethod method, string url = null)
            : this(method.ToString(), url == null ? null : new Uri(url))
        {
        }

        public ResourceRequest(Uri url, HttpMethod method)
            : this(method.ToString(), url)
        {
        }

        [JsonProperty("url")]
        public Uri Url { get; protected set; }

        [JsonProperty("met")]
        public string Method { get; protected set; }
    }
}
