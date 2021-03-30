// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reset.Models
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the reset task completes.
    public class ResetTaskResult
    {
        public ResetTaskResult(DateTimeOffset transactionTime, Uri requestUri, IList<ResetOutputResponse> output)
        {
            EnsureArg.IsNotDefault<DateTimeOffset>(transactionTime, nameof(transactionTime));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            TransactionTime = transactionTime;
            RequestUri = requestUri;
            Output = output;
        }

        [JsonConstructor]
        private ResetTaskResult()
        {
        }

        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; private set; }

        [JsonProperty("request")]
        public Uri RequestUri { get; private set; }

        [JsonProperty("output")]
        public IList<ResetOutputResponse> Output { get; private set; }
    }
}
