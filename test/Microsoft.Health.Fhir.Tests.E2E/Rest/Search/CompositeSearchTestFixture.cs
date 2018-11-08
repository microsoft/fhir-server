// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompositeSearchTestFixture : SearchTestFixture
    {
        private static readonly string[] TestFileNames = new string[]
        {
            "ObservationWith1MinuteApgarScore",
            "ObservationWith20MinuteApgarScore",
            "ObservationWithEyeColor",
            "ObservationWithTemperature",
            "ObservationWithTPMTDiplotype",
            "ObservationWithTPMTHaplotypeOne",
            "ObservationWithBloodPressure",
        };

        public IReadOnlyDictionary<string, Observation> Observations { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            var resultDictionary = new Dictionary<string, Observation>(TestFileNames.Length);

            for (int i = 0; i < TestFileNames.Length; i++)
            {
                string testFileName = TestFileNames[i];

                resultDictionary.Add(testFileName, await CreateResourceAsync<Observation>(o => o.Identifier, testFileName));
            }

            Observations = resultDictionary;
        }
    }
}
