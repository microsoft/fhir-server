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

        public ResourceRequest(Uri url, string method)
        {
            EnsureArg.IsNotNullOrEmpty(method, nameof(method));

            Url = url;
            Method = method;
        }

        public ResourceRequest(string url, HttpMethod method)
            : this(url == null ? null : new Uri(url), method.ToString())
        {
        }

        public ResourceRequest(Uri url, HttpMethod method)
            : this(url, method.ToString())
        {
        }

        [JsonProperty("url")]
        public Uri Url { get; protected set; }

        [JsonProperty("method")]
        public string Method { get; protected set; }
    }
}
