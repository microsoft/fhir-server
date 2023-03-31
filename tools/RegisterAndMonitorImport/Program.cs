// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.RegisterAndMonitorImport
{
    public static class Program
    {
        public static async Task Main()
        {
            await RegisterAndMonitorImport.Run();
        }
    }
}
