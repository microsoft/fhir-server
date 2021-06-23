// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Microsoft.Health.Fhir.Core.Features.ArtifactStore
{
    // Describes Open Container Initiative (OCI) artifacts managed in ACR
    // https://docs.microsoft.com/en-us/azure/container-registry/container-registry-oci-artifacts
    public class OciArtifactInfo
    {
        public string LoginServer { get; set; }

        public string ImageName { get; set; }

        public string Digest { get; set; }

        public bool ContainsOciImage(string server, string name, string digest)
        {
            // If LoginServer is not provided, return false because this ArtifactInfo is invalid.
            if (string.IsNullOrEmpty(LoginServer) || !string.Equals(LoginServer, server, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Compare imageName if ImageName is provided in ArtifactInfo.
            if (!string.IsNullOrEmpty(ImageName) && !string.Equals(ImageName, name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Compare digest if Digest is provided in ArtifactInfo.
            if (!string.IsNullOrEmpty(Digest) && !string.Equals(Digest, digest, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }
}
