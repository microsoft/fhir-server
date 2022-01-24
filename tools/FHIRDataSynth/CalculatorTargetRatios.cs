using ResourceProcessorNamespace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FHIRDataSynth
{
    class CalculatorTargetRatios
    {
        class TargetRatios
        {
            public List<TargetProfile> targetRatios { get; set; }
        }

        public const string ResourcesTotalSizeHeader = "ResourceName,Bytes";
        public const string OneResourceGroupInfoHeader = "Resource Name,Ids Count,Duplicate Ids Count,Patient Refs Not Patient,Subject Refs Not Patient,Patient Ref Ids Count,Intersect Patients PatientRefs Count,Lines Count,Lines Length Sum";

        public static bool IsValidBlobContainerName(string s)
        {
            return s != null && s.Length > 0 && s.All(c => char.IsDigit(c) || char.IsLower(c) || c == '-') && char.IsLetterOrDigit(s[0]) && char.IsLetterOrDigit(s[s.Length - 1]);
        }

        private class CalculationData
        {
            public int linesCount = 0;
            public long linesLengthSum = 0;
            public long resourceInputSize = 0;
            public double blendRatio = 0;
        }

        class BlendProfile
        {
            public string BlendName { get; set; }

            public Dictionary<string, double> BlendRatios { get; set; }
        }

        class BlendRatios
        {
            public BlendProfile[] BlendProfiles { get; set; }
        }

        class OutputResourceGroupSize
        {
            public int OutputResourceGroupsCount { get; }

            public double GBPerResourceGroup { get; }

            public double BytesPerResourceGroup { get => GBPerResourceGroup * 1024 * 1024 * 1024; }

            public string Text { get; }

            public OutputResourceGroupSize(double gbPerResourceGroup, string text, int outputResourceGroupsCount)
            {
                GBPerResourceGroup = gbPerResourceGroup;
                Text = text;
                OutputResourceGroupsCount = outputResourceGroupsCount;
            }
        }

        public static void Calculate(string blobGroupsInfoPath, string oneGroupInfoPath, string blendRatiosFilePath, string targetRatiosPath, string targetRatiosPathCsv)
        {
            if (Directory.Exists(targetRatiosPath))
            {
                throw new Exception($"Directory {targetRatiosPath} exists!");
            }

            if (File.Exists(targetRatiosPath))
            {
                throw new Exception($"File {targetRatiosPath} already exists!");
            }

            if (Directory.Exists(targetRatiosPathCsv))
            {
                throw new Exception($"Directory {targetRatiosPathCsv} exists!");
            }

            if (File.Exists(targetRatiosPathCsv))
            {
                throw new Exception($"File {targetRatiosPathCsv} already exists!");
            }

            BlendRatios blendRatios;
            try
            {
                string text = File.ReadAllText(blendRatiosFilePath);
                blendRatios = JsonSerializer.Deserialize<BlendRatios>(text);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing file {blendRatiosFilePath}. ({ex.Message})", ex);
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
                targetRatios.targetRatios = new List<TargetProfile>();
                foreach (BlendProfile blendProfile in blendRatios.BlendProfiles)
                {
                    if (!IsValidBlobContainerName(blendProfile.BlendName))
                    {
                        throw new Exception($"Invalid blend profile name '{blendProfile.BlendName}' in file '{blendRatiosFilePath}'. Follow Azure Blob naming rules.");
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
                            out TargetProfile targetProfile);
                        targetRatios.targetRatios.Add(targetProfile);
                    }
                }

                string targetRatiosStr = JsonSerializer.Serialize(targetRatios, new JsonSerializerOptions() { WriteIndented = true });
                File.WriteAllText(targetRatiosPath, targetRatiosStr);
            }
        }

        delegate int GetResourceSizeDelegate();

        private static void AddCalculationDataX(BlendProfile blendProfile, Dictionary<string, CalculationData> calculationData, GetResourceSizeDelegate GetResourceSize, string resourceName)
        {
            int resourceSize = GetResourceSize();
            CalculationData cd = new CalculationData();
            cd.resourceInputSize = resourceSize * (long)usedResourceGroupsCount;
            cd.linesCount = 1;
            cd.linesLengthSum = resourceSize;
            cd.blendRatio = 0;
            if (blendProfile.BlendRatios.TryGetValue(resourceName, out double blendRatio))
            {
                cd.blendRatio = blendRatio;
            }

            calculationData.Add(resourceName, cd);
        }

        const double usedResourceGroupsCount = 800;

        private static void CalculateRatios(
            OutputResourceGroupSize outputResourceGroupSize,
            double actualResourceGroupsCount,
            BlendProfile blendProfile,
            string blobGroupsInfoPath,
            string oneGroupInfoPath,
            StreamWriter streamWriter,
            out ResourceProcessorNamespace.TargetProfile targeProfile)
        {
            Dictionary<string, CalculationData> calculationData = new Dictionary<string, CalculationData>();

            using (StreamReader streamReader = new StreamReader(blobGroupsInfoPath))
            {
                string line = streamReader.ReadLine();
                if (line != ResourcesTotalSizeHeader)
                {
                    throw new Exception($"Invalid header in '{blobGroupsInfoPath}'!");
                }

                while ((line = streamReader.ReadLine()) != null)
                {
                    string[] fields = line.Split(',');
                    if (fields.Length != 2)
                    {
                        throw new Exception($"Invalid number of columns in '{blobGroupsInfoPath}'!");
                    }

                    string key = fields[0];
                    string value = fields[1];
                    calculationData[key] = new CalculationData();

                    // TODO: Use only 800 out of 803 resource groups. This way we can create different db sizes by simply using different
                    // number of blended resource groups. For example if blended resource group size is 2.5GB then we use 4 resource groups for 10 GB db,
                    // 40 for 100GB, 400for 1TB and all 800 for 2TB. blobGroupsInfoPath should point to a json file instead of csv and should contain
                    // number of resource groups (803 at the moment).
                    calculationData[key].resourceInputSize = (long)((double)long.Parse(value) * (usedResourceGroupsCount / actualResourceGroupsCount));
                    if (blendProfile.BlendRatios.TryGetValue(key, out double blendRatio))
                    {
                        calculationData[key].blendRatio = blendRatio;
                    }
                }
            }

            SortedSet<string> oneGroupResources = new SortedSet<string>();
            using (StreamReader streamReader = new StreamReader(oneGroupInfoPath))
            {
                string line = streamReader.ReadLine();
                if (line != OneResourceGroupInfoHeader)
                {
                    throw new Exception($"Invalid header in '{blobGroupsInfoPath}'!");
                }

                while ((line = streamReader.ReadLine()) != null)
                {
                    string[] fields = line.Split(',');
                    if (fields.Length != 9)
                    {
                        throw new Exception($"Invalid number of columns in '{blobGroupsInfoPath}'!");
                    }

                    string key = fields[0];
                    if (!calculationData.ContainsKey(key))
                    {
                        throw new Exception($"Mismatch between resource names in files '{blobGroupsInfoPath}' and '{oneGroupInfoPath}'");
                    }

                    calculationData[key].linesCount = int.Parse(fields[7]);
                    calculationData[key].linesLengthSum = long.Parse(fields[8]);
                    oneGroupResources.Add(key);
                }
            }

            if (oneGroupResources.Count != oneGroupResources.Intersect(calculationData.Keys).Count())
            {
                throw new Exception($"Mismatch between resource names in files '{blobGroupsInfoPath}' and '{oneGroupInfoPath}'");
            }

            AddCalculationDataX(blendProfile, calculationData, CommunicationAdapter.Enumerator.GetResourceSize, "Communication");
            AddCalculationDataX(blendProfile, calculationData, DocumentReferenceAdapter.Enumerator.GetResourceSize, "DocumentReference");
            AddCalculationDataX(blendProfile, calculationData, StructureDefinitionAdapter.Enumerator.GetResourceSize, "StructureDefinition");

            // Done with data loading. Now do calculations.

            // Normalize blend ratio just in case.
            double sumBlend = calculationData.Sum(d => d.Value.blendRatio);
            foreach (var d in calculationData)
            {
                d.Value.blendRatio = d.Value.blendRatio / sumBlend;
            }

            // TODO, take into account newline in blob.
            // TODO, take into account data compression in FHIR server.

            streamWriter.WriteLine("Blend Name,Resource Groups Count,Average Resource Group Size GB,Max DB Size GB,Resource,Normalized Blend Ratio (Sum=1),Synthea Total Size,Synthea Total Count,Synthea First Group Size,Synthea First Group Count,Average Size,Resource DB Size,Resource DB Count,DB/Synthea,Resources Created Or Deleted(-)");
            double sumResourceAvgSizeByBlendRatio = 0;
            foreach (KeyValuePair<string, CalculationData> data in calculationData)
            {
                CalculationData d = data.Value;
                double resourceAvgSize = ((double)d.linesLengthSum) / d.linesCount; // Resource average size calculated from the first resource group.
                double resourceAvgSizeByBlendRatio = resourceAvgSize * d.blendRatio; // Scaled by blend ratio, gives ratio between resource type total sizes.
                sumResourceAvgSizeByBlendRatio += resourceAvgSizeByBlendRatio;
            }

            targeProfile = new TargetProfile();
            targeProfile.name = blendProfile.BlendName + "-" + outputResourceGroupSize.Text;
            foreach (KeyValuePair<string, CalculationData> data in calculationData)
            {
                CalculationData d = data.Value;
                double resourceAvgSize = ((double)d.linesLengthSum) / d.linesCount; // Resource average size calculated from the first resource group.
                double resourceAvgSizeByBlendRatio = resourceAvgSize * d.blendRatio; // Scaled by blend ratio, gives ratio between resource type total sizes.
                double resourceOutputSize = (outputResourceGroupSize.BytesPerResourceGroup * usedResourceGroupsCount) * (resourceAvgSizeByBlendRatio / sumResourceAvgSizeByBlendRatio); // Resource type total size for all resource groups.
                double resourceOutputCount = resourceOutputSize / resourceAvgSize; // Resource type total count for all resource groups.
                double resourceOutputInputRatio = resourceOutputSize / d.resourceInputSize;
                double resourceInputCount = d.resourceInputSize / resourceAvgSize;
                double resourcesToBeCreatedOrDeleted = resourceOutputCount - resourceInputCount;
                streamWriter.WriteLine($"{targeProfile.name},{usedResourceGroupsCount},{outputResourceGroupSize.GBPerResourceGroup},{outputResourceGroupSize.GBPerResourceGroup * usedResourceGroupsCount},{data.Key},{d.blendRatio},{d.resourceInputSize},{d.resourceInputSize / resourceAvgSize},{d.linesLengthSum},{d.linesCount},{resourceAvgSize},{resourceOutputSize},{resourceOutputCount},{resourceOutputInputRatio},{resourcesToBeCreatedOrDeleted}");
                targeProfile.ratios[data.Key] = resourceOutputInputRatio;
                targeProfile.resourceGroupsCount = outputResourceGroupSize.OutputResourceGroupsCount;
            }
        }
    }
}
