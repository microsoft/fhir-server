// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SchemaInformation
    {
        public SchemaInformation()
        {
            MinimumSupportedVersion = (int)SchemaVersion.V1;
            MaximumSupportedVersion = (int)SchemaVersion.V3;
        }

        public int MinimumSupportedVersion { get; }

        public int MaximumSupportedVersion { get; }

        public int? Current { get; set; }
    }
}
