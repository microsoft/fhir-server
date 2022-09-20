// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.PostgresQL
{
    public static class PostgresQLConfiguration
    {
        public static readonly string DefaultConnectionString = "Host=localhost;Port=5432;Username=postgres;";

        public static readonly Dictionary<string, short> ResourceTypeIdMap = new Dictionary<string, short>() { { "Patient", 103 } };
    }
}
