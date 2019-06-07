// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class TestRun
    {
        public object Conformance { get; set; }

        public DateTime? Date { get; set; }

        [JsonProperty("destination_conformance")]
        public object DestinationConformance { get; set; }

        [JsonProperty("destination_server_id")]
        public object DestinationServerId { get; set; }

        [JsonProperty("fhir_version")]
        public string FhirVersion { get; set; }

        [JsonProperty("is_multiserver")]
        public bool IsMultiserver { get; set; }

        [JsonProperty("last_updated")]
        public DateTime? LastUpdated { get; set; }

        [JsonProperty("nightly")]
        public bool Nightly { get; set; }

        [JsonProperty("server_id")]
        public ServerId ServerId { get; set; }

        public string Status { get; set; }

        [JsonProperty("supported_only")]
        public bool SupportedOnly { get; set; }

        [JsonProperty("test_ids")]
        public string[] TestIds { get; set; }

        [JsonProperty("worker_id")]
        public string WorkerId { get; set; }

        public string Id { get; set; }

        [JsonProperty("test_results")]
        public TestResults[] TestResults { get; set; }
    }
}
