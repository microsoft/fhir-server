// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.BulkImport
{
    public class CreateBulkImportRequest : IRequest<CreateBulkImportResponse>
    {
        public CreateBulkImportRequest(
            Uri requestUri,
            BulkImportRequestConfiguration requestConfiguration)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            RequestConfiguration = requestConfiguration;
        }

        public Uri RequestUri { get; }

        public BulkImportRequestConfiguration RequestConfiguration { get; }
    }
}
