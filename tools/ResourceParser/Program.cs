// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace ResourceParser
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var parser = new Microsoft.Health.Fhir.R4.ResourceParser.ResourceWrapperParser();

            // string input = "{\"resourceType\": \"Patient\", \"gender\": \"male\", \"id\": \"123\"}";
            string input = args[0];
            var resourceWrapper = parser.CreateResourceWrapper(input);
            var output = parser.SerializeToString(resourceWrapper);
            Console.Write(output);
        }
    }
}
