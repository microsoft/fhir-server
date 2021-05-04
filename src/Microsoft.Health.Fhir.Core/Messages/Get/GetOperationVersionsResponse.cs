// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetOperationVersionsResponse
    {
        public GetOperationVersionsResponse(ICollection<string> supportedVersions, string defaultVersion)
        {
            EnsureArg.IsNotNull(supportedVersions, nameof(supportedVersions));
            EnsureArg.IsNotNullOrWhiteSpace(defaultVersion, nameof(defaultVersion));

            SupportedVersions = supportedVersions;
            DefaultVersion = defaultVersion;
        }

        public ICollection<string> SupportedVersions { get; }

        public string DefaultVersion { get; }
    }
}
