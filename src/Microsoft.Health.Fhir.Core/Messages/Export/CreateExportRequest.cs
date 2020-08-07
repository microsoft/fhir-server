// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportRequest : IRequest<CreateExportResponse>
    {
        public CreateExportRequest(Uri requestUri, ExportJobType requestType, string resourceType = null, PartialDateTime since = null)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            RequestType = requestType;
            ResourceType = resourceType;
            Since = since;
        }

        public Uri RequestUri { get; }

        public ExportJobType RequestType { get; }

        public string ResourceType { get; }

        public PartialDateTime Since { get; }
    }
}
