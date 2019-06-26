// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class TestRunResponse
    {
        [JsonProperty("test_run")]
        public TestRun TestRun { get; set; }
    }
}
