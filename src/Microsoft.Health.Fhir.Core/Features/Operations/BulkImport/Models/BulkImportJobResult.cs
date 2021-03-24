// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the
    /// bulk impoty job completes. This is a subset of the data present in <see cref="BulkImportJobRecord"/>.
    /// </summary>
    public class BulkImportJobResult
    {
        public BulkImportJobResult(DateTimeOffset transactionTime, Uri requestUri, IList<BulkImportOutputResponse> output, IList<BulkImportOutputResponse> errors, IList<Core.Models.OperationOutcomeIssue> issues = null)
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
        private BulkImportJobResult()
        {
        }

        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; private set; }

        [JsonProperty("request")]
        public Uri RequestUri { get; private set; }

        [JsonProperty("output")]
        public IList<BulkImportOutputResponse> Output { get; private set; }

        [JsonProperty("error")]
        public IList<BulkImportOutputResponse> Error { get; private set; }

        [JsonProperty("issues")]
        public IList<Microsoft.Health.Fhir.Core.Models.OperationOutcomeIssue> Issues { get; private set; }
    }
}
