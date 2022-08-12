// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace ResourceParser
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Startup();
        }

        public static void Startup()
        {
            var referenceSearchValueParser = new ReferenceSearchValueParser();

        }
    }
}
