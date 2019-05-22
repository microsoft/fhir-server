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
        public CreateExportRequest(Uri requestUri, string destinationType, string destinationConnectionString)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(destinationType, nameof(destinationType));
            EnsureArg.IsNotNullOrWhiteSpace(destinationConnectionString, nameof(destinationConnectionString));

            RequestUri = requestUri;
            DestinationInfo = new DestinationInfo(destinationType, destinationConnectionString);
        }

        public Uri RequestUri { get; }

        public DestinationInfo DestinationInfo { get; }
    }
}
