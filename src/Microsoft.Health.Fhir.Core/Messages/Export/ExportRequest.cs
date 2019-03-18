// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class ExportRequest : IRequest<ExportResponse>
    {
        public ExportRequest(Uri requestUri)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
        }

        public Uri RequestUri { get; }
    }
}
