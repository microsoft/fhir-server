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
            MinimumSupportedVersion = SchemaVersion.V1;
            MaximumSupportedVersion = SchemaVersion.V1;
        }

        public SchemaVersion MinimumSupportedVersion { get; }

        public SchemaVersion MaximumSupportedVersion { get; }

        public SchemaVersion? Current { get; set; }
    }
}
