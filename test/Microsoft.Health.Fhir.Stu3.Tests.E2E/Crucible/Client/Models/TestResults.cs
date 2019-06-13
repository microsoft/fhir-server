// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class TestResults
    {
        public string Id { get; set; }

        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonProperty("has_run")]
        public bool HasRun { get; set; }

        public Result[] Result { get; set; }

        [JsonProperty("server_id")]
        public OId ServerId { get; set; }

        [JsonProperty("setup_message")]
        public object SetupMessage { get; set; }

        [JsonProperty("setup_requests")]
        public object[] SetupRequests { get; set; }

        [JsonProperty("teardown_requests")]
        public RequestInfo[] TeardownRequests { get; set; }

        [JsonProperty("test_id")]
        public string TestId { get; set; }

        [JsonProperty("test_run_id")]
        public OId TestRunId { get; set; }

        [JsonProperty("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
