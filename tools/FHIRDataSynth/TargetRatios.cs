// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace ResourceProcessorNamespace
{
    internal class TargetRatios
    {
#pragma warning disable SA1300 // JSON serialization/de-serialization, follow JSON naming convention.
        public List<TargetProfile> targetRatios { get; set; }
#pragma warning restore SA1300

        public class TargetProfile
        {
#pragma warning disable SA1300 // JSON serialization/de-serialization, follow JSON naming convention.
            public string name { get; set; }

            public int resourceGroupsCount { get; set; }

            public Dictionary<string, double> ratios { get; set; } = new Dictionary<string, double>();
#pragma warning restore SA1300

            public void Validate()
            {
                if (resourceGroupsCount < 0)
                {
                    throw new FHIRDataSynth.FHIRDataSynthException($"TargetProfil member 'resourceGroupsCount' contains invalid value {resourceGroupsCount}, must be 0 or greater.");
                }

                foreach (KeyValuePair<string, double> r in ratios)
                {
                    if (r.Value < 0)
                    {
                        throw new FHIRDataSynth.FHIRDataSynthException($"TargetProfile member 'ratios[{r.Key}]' contains invalid value {r.Value}, must 0 or greater.");
                    }
                }
            }
        }
    }
}
