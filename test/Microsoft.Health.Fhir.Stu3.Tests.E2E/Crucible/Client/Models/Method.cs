// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Method
    {
        public string Key { get; set; }

        public string Id { get; set; }

        public string Description { get; set; }

        public Require[] Requires { get; set; }

        public Validate[] Validates { get; set; }

        public string[] Links { get; set; }

        [JsonProperty("test_method")]
        public string TestMethod { get; set; }

        public bool Supported { get; set; }
    }
}
