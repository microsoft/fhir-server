// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportRequest : IRequest<CreateExportResponse>
    {
        public CreateExportRequest(Uri requestUri, string destinationType, string destinationConnectionString, string resourceType = null, bool useConfig = false)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            DestinationInfo = new DestinationInfo(destinationType, destinationConnectionString);
            ResourceType = resourceType;
            UseConfigFlag = useConfig;
        }

        public Uri RequestUri { get; }

        public DestinationInfo DestinationInfo { get; }

        public string ResourceType { get; }

        public bool UseConfigFlag { get; }
    }
}
