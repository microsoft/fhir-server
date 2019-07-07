// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models
{
    public class Result
    {
        public string Key { get; set; }

        public string Id { get; set; }

        public string Description { get; set; }

        public string Status { get; set; }

        public object Message { get; set; }

        public Validate[] Validates { get; set; }

        public string[] Links { get; set; }

        public string Code { get; set; }

        [JsonProperty("test_method")]
        public string TestMethod { get; set; }

        public RequestInfo[] Requests { get; set; }

        [JsonProperty("test_result_id")]
        public OId TestResultId { get; set; }

        [JsonProperty("test_id")]
        public string TestId { get; set; }

        public DateTime CreatedAt { get; set; }

        public string[] Warnings { get; set; }
    }
}
