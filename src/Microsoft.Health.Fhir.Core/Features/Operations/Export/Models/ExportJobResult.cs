// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the
    /// export job completes. This is a subset of the data present in <see cref="ExportJobRecord"/>.
    /// </summary>
    public class ExportJobResult
    {
        public ExportJobResult(DateTimeOffset transactionTime, Uri requestUri, bool requiresAccessToken, IList<ExportFileInfo> output, IList<ExportFileInfo> errors)
        {
            EnsureArg.IsNotDefault<DateTimeOffset>(transactionTime, nameof(transactionTime));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNull(output, nameof(output));
            EnsureArg.IsNotNull(errors, nameof(errors));

            TransactionTime = transactionTime;
            RequestUri = requestUri;
            RequiresAccessToken = requiresAccessToken;
            Output = output;
            Error = errors;
        }

        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; }

        [JsonProperty("request")]
        public Uri RequestUri { get; }

        [JsonProperty("requiresAccessToken")]
        public bool RequiresAccessToken { get; }

        [JsonProperty("output")]
        public IList<ExportFileInfo> Output { get; }

        [JsonProperty("error")]
        public IList<ExportFileInfo> Error { get; }
    }
}
