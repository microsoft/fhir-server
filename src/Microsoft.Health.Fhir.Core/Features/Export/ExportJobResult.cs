// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobResult
    {
        public ExportJobResult(Instant transactionTime, Uri requestUri, bool requiresAccessToken, IList<ExportFileInfo> output, IList<ExportFileInfo> errors)
        {
            EnsureArg.IsNotNull(transactionTime, nameof(transactionTime));
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNull(output, nameof(output));
            EnsureArg.IsNotNull(errors, nameof(errors));

            TransactionTime = transactionTime;
            RequestUri = requestUri;
            RequiresAccessToken = requiresAccessToken;
            Output = output;
            Errors = errors;
        }

        public Instant TransactionTime { get; }

        public Uri RequestUri { get; }

        public bool RequiresAccessToken { get; }

        public IList<ExportFileInfo> Output { get; }

        public IList<ExportFileInfo> Errors { get; }
    }
}
