// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Ping
{
    public static class Program
    {
        private static readonly HttpClient HttpClient = new();

        public static void Main(string[] args)
        {
            var uri = args.Length > 0 ? args[0] : "http://localhost:5000/Observation?_summary=count&_lastUpdated=lt2020-01-01";
            var delay = args.Length > 1 ? int.Parse(args[1]) : 1000;
            Console.WriteLine($"Delay={delay} Uri={uri}");
            while (true)
            {
                Console.WriteLine($"response.StatusCode={HttpClient.GetAsync(new Uri(uri)).Result.StatusCode}");
                Thread.Sleep(delay);
            }
        }
    }
}
