// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Routing.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;

namespace Microsoft.Health.Fhir.Api.Operations.Versions
{
    public class GetOperationVersionsResponse : IOperationActionResultResponse
    {
        public GetOperationVersionsResponse(List<string> supportedVersions, string defaultVersion)
        {
            EnsureArg.IsNotNull(supportedVersions, nameof(supportedVersions));
            EnsureArg.IsNotNullOrWhiteSpace(defaultVersion, nameof(defaultVersion));

            SupportedVersions = supportedVersions;
            DefaultVersion = defaultVersion;
        }

        public List<string> SupportedVersions { get; }

        public string DefaultVersion { get; }

        public HttpStatusCode StatusCode => HttpStatusCode.OK;

        public IActionResult Response => new OperationVersionsResult(new VersionsResult(SupportedVersions, DefaultVersion), StatusCode);
    }
}
