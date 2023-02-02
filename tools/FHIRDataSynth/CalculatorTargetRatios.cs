// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal static class CalculatorTargetRatios
    {
        public const string ResourcesTotalSizeHeader = "ResourceName,Bytes";
        public const string OneResourceGroupInfoHeader = "Resource Name,Ids Count,Duplicate Ids Count,Patient Refs Not Patient,Subject Refs Not Patient,Patient Ref Ids Count,Intersect Patients PatientRefs Count,Lines Count,Lines Length Sum";
        private const double UsedResourceGroupsCount = 800;

        private delegate int GetResourceSizeDelegate();

        public static bool IsValidBlobContainerName(string s)
        {
            return s != null && s.Length > 0 && s.All(c => char.IsDigit(c) || char.IsLower(c) || c == '-') && char.IsLetterOrDigit(s[0]) && char.IsLetterOrDigit(s[s.Length - 1]);
        }

        public static void Calculate(string blobGroupsInfoPath, string oneGroupInfoPath, string blendRatiosFilePath, string targetRatiosPath, string targetRatiosPathCsv)
        {
            if (Directory.Exists(targetRatiosPath))
            {
                throw new FHIRDataSynthException($"Directory {targetRatiosPath} exists!");
            }

            if (File.Exists(targetRatiosPath))
            {
                throw new FHIRDataSynthException($"File {targetRatiosPath} already exists!");
            }

            if (Directory.Exists(targetRatiosPathCsv))
            {
                throw new FHIRDataSynthException($"Directory {targetRatiosPathCsv} exists!");
            }

            if (File.Exists(targetRatiosPathCsv))
            {
                throw new FHIRDataSynthException($"File {targetRatiosPathCsv} already exists!");
            }

            BlendRatios blendRatios;
            try
            {
                string text = File.ReadAllText(blendRatiosFilePath);
                blendRatios = JsonSerializer.Deserialize<BlendRatios>(text);
            }
            catch (Exception ex)
            {
                throw new FHIRDataSynthException($"Error parsing file {blendRatiosFilePath}. ({ex.Message})", ex);
            }

            OutputResourceGroupSize[] outputResourceGroupSizes = new OutputResourceGroupSize[4]
            {
                // For text follow Azure Blob Container naming rules.
                new OutputResourceGroupSize(0.01, "0-01gb-prg", 1),
                new OutputResourceGroupSize(1, "1gb-prg", 1),
                new OutputResourceGroupSize(2.5, "2-5gb-prg", 1),
                new OutputResourceGroupSize(5, "5gb-prg", 1),
            };

            using (StreamWriter streamWriter = new StreamWriter(targetRatiosPathCsv))
            {
                TargetRatios targetRatios = new TargetRatios();
                targetRatios.targetRatios = new List<TargetRatios.TargetProfile>();
                foreach (BlendProfile blendProfile in blendRatios.BlendProfiles)
                {
                    if (!IsValidBlobContainerName(blendProfile.BlendName))
                    {
                        throw new FHIRDataSynthException($"Invalid blend profile name '{blendProfile.BlendName}' in file '{blendRatiosFilePath}'. Follow Azure Blob naming rules.");
                    }

                    foreach (OutputResourceGroupSize outputResourceGroupSize in outputResourceGroupSizes)
                    {
                        CalculateRatios(
                            outputResourceGroupSize,
                            803,
                            blendProfile,
                            blobGroupsInfoPath,
                            oneGroupInfoPath,
                            streamWriter,
                            out TargetRatios.TargetProfile targetProfile);
                        targetRatios.targetRatios.Add(targetProfile);
                    }
                }

                string targetRatiosStr = JsonSerializer.Serialize(targetRatios, new JsonSerializerOptions() { WriteIndented = true });
                File.WriteAllText(targetRatiosPath, targetRatiosStr);
            }
        }

        private static void AddCalculationDataX(BlendProfile blendProfile, Dictionary<string, CalculationData> calculationData, GetResourceSizeDelegate getResourceSize, string resourceName)
        {
            int resourceSize = getResourceSize();
            CalculationData cd = new CalculationData();
            cd.ResourceInputSize = resourceSize * (long)UsedResourceGroupsCount;
            cd.LinesCount = 1;
            cd.LinesLengthSum = resourceSize;
            cd.BlendRatio = 0;
            if (blendProfile.BlendRatios.TryGetValue(resourceName, out double blendRatio))
            {
                cd.BlendRatio = blendRatio;
            }

            calculationData.Add(resourceName, cd);
        }

        private static void CalculateRatios(
            OutputResourceGroupSize outputResourceGroupSize,
            double actualResourceGroupsCount,
            BlendProfile blendProfile,
            string blobGroupsInfoPath,
            string oneGroupInfoPath,
            StreamWriter streamWriter,
            out TargetRatios.TargetProfile targeProfile)
        {
            Dictionary<string, CalculationData> calculationData = new Dictionary<string, CalculationData>();

            using (StreamReader streamReader = new StreamReader(blobGroupsInfoPath))
            {
                string line = streamReader.ReadLine();
                if (line != ResourcesTotalSizeHeader)
                {
                    throw new FHIRDataSynthException($"Invalid header in '{blobGroupsInfoPath}'!");
                }

                while ((line = streamReader.ReadLine()) != null)
                {
                    string[] fields = line.Split(',');
                    if (fields.Length != 2)
                    {
                        throw new FHIRDataSynthException($"Invalid number of columns in '{blobGroupsInfoPath}'!");
                    }

                    string key = fields[0];
                    string value = fields[1];
                    calculationData[key] = new CalculationData();

                    // TODO: Use only 800 out of 803 resource groups. This way we can create different db sizes by simply using different
                    // number of blended resource groups. For example if blended resource group size is 2.5GB then we use 4 resource groups for 10 GB db,
                    // 40 for 100GB, 400for 1TB and all 800 for 2TB. blobGroupsInfoPath should point to a json file instead of csv and should contain
                    // number of resource groups (803 at the moment).
                    calculationData[key].ResourceInputSize = (long)(long.Parse(value) * (UsedResourceGroupsCount / actualResourceGroupsCount));
                    if (blendProfile.BlendRatios.TryGetValue(key, out double blendRatio))
                    {
                        calculationData[key].BlendRatio = blendRatio;
                    }
                }
            }

            SortedSet<string> oneGroupResources = new SortedSet<string>();
            using (StreamReader streamReader = new StreamReader(oneGroupInfoPath))
            {
                string line = streamReader.ReadLine();
                if (line != OneResourceGroupInfoHeader)
                {
                    throw new FHIRDataSynthException($"Invalid header in '{blobGroupsInfoPath}'!");
                }

                while ((line = streamReader.ReadLine()) != null)
                {
                    string[] fields = line.Split(',');
                    if (fields.Length != 9)
                    {
                        throw new FHIRDataSynthException($"Invalid number of columns in '{blobGroupsInfoPath}'!");
                    }

                    string key = fields[0];
                    if (!calculationData.ContainsKey(key))
                    {
                        throw new FHIRDataSynthException($"Mismatch between resource names in files '{blobGroupsInfoPath}' and '{oneGroupInfoPath}'");
                    }

                    calculationData[key].LinesCount = int.Parse(fields[7]);
                    calculationData[key].LinesLengthSum = long.Parse(fields[8]);
                    oneGroupResources.Add(key);
                }
            }

            if (oneGroupResources.Count != oneGroupResources.Intersect(calculationData.Keys).Count())
            {
                throw new FHIRDataSynthException($"Mismatch between resource names in files '{blobGroupsInfoPath}' and '{oneGroupInfoPath}'");
            }

            AddCalculationDataX(blendProfile, calculationData, ResourceXCommunicationAdapter.Enumerator.GetResourceSize, "Communication");
            AddCalculationDataX(blendProfile, calculationData, ResourceXDocumentReferenceAdapter.Enumerator.GetResourceSize, "DocumentReference");
            AddCalculationDataX(blendProfile, calculationData, ResourceXStructureDefinitionAdapter.Enumerator.GetResourceSize, "StructureDefinition");

            // Done with data loading. Now do calculations.

            // Normalize blend ratio just in case.
            double sumBlend = calculationData.Sum(d => d.Value.BlendRatio);
            foreach (var d in calculationData)
            {
                d.Value.BlendRatio = d.Value.BlendRatio / sumBlend;
            }

            // TODO, take into account newline in blob.
            // TODO, take into account data compression in FHIR server.

            streamWriter.WriteLine("Blend Name,Resource Groups Count,Average Resource Group Size GB,Max DB Size GB,Resource,Normalized Blend Ratio (Sum=1),Synthea Total Size,Synthea Total Count,Synthea First Group Size,Synthea First Group Count,Average Size,Resource DB Size,Resource DB Count,DB/Synthea,Resources Created Or Deleted(-)");
            double sumResourceAvgSizeByBlendRatio = 0;
            foreach (KeyValuePair<string, CalculationData> data in calculationData)
            {
                CalculationData d = data.Value;
                double resourceAvgSize = ((double)d.LinesLengthSum) / d.LinesCount; // Resource average size calculated from the first resource group.
                double resourceAvgSizeByBlendRatio = resourceAvgSize * d.BlendRatio; // Scaled by blend ratio, gives ratio between resource type total sizes.
                sumResourceAvgSizeByBlendRatio += resourceAvgSizeByBlendRatio;
            }

            targeProfile = new TargetRatios.TargetProfile();
            targeProfile.name = blendProfile.BlendName + "-" + outputResourceGroupSize.Text;
            foreach (KeyValuePair<string, CalculationData> data in calculationData)
            {
                CalculationData d = data.Value;
                double resourceAvgSize = ((double)d.LinesLengthSum) / d.LinesCount; // Resource average size calculated from the first resource group.
                double resourceAvgSizeByBlendRatio = resourceAvgSize * d.BlendRatio; // Scaled by blend ratio, gives ratio between resource type total sizes.
                double resourceOutputSize = (outputResourceGroupSize.BytesPerResourceGroup * UsedResourceGroupsCount) * (resourceAvgSizeByBlendRatio / sumResourceAvgSizeByBlendRatio); // Resource type total size for all resource groups.
                double resourceOutputCount = resourceOutputSize / resourceAvgSize; // Resource type total count for all resource groups.
                double resourceOutputInputRatio = resourceOutputSize / d.ResourceInputSize;
                double resourceInputCount = d.ResourceInputSize / resourceAvgSize;
                double resourcesToBeCreatedOrDeleted = resourceOutputCount - resourceInputCount;
                streamWriter.WriteLine($"{targeProfile.name},{UsedResourceGroupsCount},{outputResourceGroupSize.GBPerResourceGroup},{outputResourceGroupSize.GBPerResourceGroup * UsedResourceGroupsCount},{data.Key},{d.BlendRatio},{d.ResourceInputSize},{d.ResourceInputSize / resourceAvgSize},{d.LinesLengthSum},{d.LinesCount},{resourceAvgSize},{resourceOutputSize},{resourceOutputCount},{resourceOutputInputRatio},{resourcesToBeCreatedOrDeleted}");
                targeProfile.ratios[data.Key] = resourceOutputInputRatio;
                targeProfile.resourceGroupsCount = outputResourceGroupSize.OutputResourceGroupsCount;
            }
        }

        private sealed class CalculationData
        {
            public int LinesCount { get; set; }

            public long LinesLengthSum { get; set; }

            public long ResourceInputSize { get; set; }

            public double BlendRatio { get; set; }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
        private sealed class BlendProfile
#pragma warning restore CA1812
        {
            public string BlendName { get; set; }

            public Dictionary<string, double> BlendRatios { get; set; }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
        private sealed class BlendRatios
#pragma warning restore CA1812
        {
            public BlendProfile[] BlendProfiles { get; set; }
        }

        private sealed class OutputResourceGroupSize
        {
            public OutputResourceGroupSize(double gbPerResourceGroup, string text, int outputResourceGroupsCount)
            {
                GBPerResourceGroup = gbPerResourceGroup;
                Text = text;
                OutputResourceGroupsCount = outputResourceGroupsCount;
            }

            public int OutputResourceGroupsCount { get; }

            public double GBPerResourceGroup { get; }

            public double BytesPerResourceGroup { get => GBPerResourceGroup * 1024 * 1024 * 1024; }

            public string Text { get; }
        }
    }
}
