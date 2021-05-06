// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportTaskResult
    {
        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; set; }

        [JsonProperty("request")]
        public string Request { get; set; }

        [JsonProperty("output")]
        public IReadOnlyCollection<ImportOperationOutcome> Output { get; set; }

        [JsonProperty("error")]
        public IReadOnlyCollection<ImportOperationOutcome> Error { get; set; }
    }
}
