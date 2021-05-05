// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import.Models
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the bulk import task completes.
    /// </summary>
    public class BulkImportTaskResult
    {
        public BulkImportTaskResult(DateTimeOffset transactionTime, Uri requestUri, IList<ImportOutputResponse> output, IList<ImportOutputResponse> errors, IList<Core.Models.OperationOutcomeIssue> issues = null)
        {
            EnsureArg.IsNotDefault<DateTimeOffset>(transactionTime, nameof(transactionTime));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNull(output, nameof(output));
            EnsureArg.IsNotNull(errors, nameof(errors));

            TransactionTime = transactionTime;
            RequestUri = requestUri;
            Output = output;
            Error = errors;
            Issues = issues;
        }

        [JsonConstructor]
        private BulkImportTaskResult()
        {
        }

        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; private set; }

        [JsonProperty("request")]
        public Uri RequestUri { get; private set; }

        [JsonProperty("output")]
        public IList<ImportOutputResponse> Output { get; private set; }

        [JsonProperty("error")]
        public IList<ImportOutputResponse> Error { get; private set; }

        [JsonProperty("issues")]
        public IList<Microsoft.Health.Fhir.Core.Models.OperationOutcomeIssue> Issues { get; private set; }
    }
}
