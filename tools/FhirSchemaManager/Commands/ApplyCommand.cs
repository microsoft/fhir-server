// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace FhirSchemaManager.Commands
{
    public static class ApplyCommand
    {
        public static void Handler(string connectionString, Uri fhirServer, int version)
        {
            Console.WriteLine($"--connection-string {connectionString}");
            Console.WriteLine($"--fhir-server {fhirServer}");
            Console.WriteLine($"--version {version}");
        }
    }
}
