// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.PostgresQL
{
    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine(PostgresQLQuery.GetData());

            PostgresQLQuery.UpsertData();
        }
    }
}
