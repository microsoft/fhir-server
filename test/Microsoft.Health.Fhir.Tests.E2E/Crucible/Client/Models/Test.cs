// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Test
    {
        public string Author { get; set; }

        public Category Category { get; set; }

        public string Description { get; set; }

        public Details Details { get; set; }

        public string Id { get; set; }

        public object Links { get; set; }

        [JsonProperty("load_version")]
        public int LoadVersion { get; set; }

        public Method[] Methods { get; set; }

        public bool Multiserver { get; set; }

        public string Name { get; set; }

        public object Requires { get; set; }

        [JsonProperty("resource_class")]
        public string ResourceClass { get; set; }

        public bool Supported { get; set; }

        [JsonProperty("supported_versions")]
        public string[] SupportedVersions { get; set; }

        public string[] Tags { get; set; }

        public string Title { get; set; }

        public object Validates { get; set; }
    }
}
