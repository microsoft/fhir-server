// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace ResourceProcessorNamespace
{
    internal abstract class ResourceGroupProcessor
    {
        public const string AllergyIntoleranceStr = "AllergyIntolerance";
        public const string AllergyIntolerancePrefix = AllergyIntoleranceStr + "/";
        public const string CarePlanStr = "CarePlan";
        public const string CarePlanPrefix = CarePlanStr + "/";
        public const string CareTeamStr = "CareTeam";
        public const string CareTeamPrefix = CareTeamStr + "/";
        public const string ClaimStr = "Claim";
        public const string ClaimPrefix = ClaimStr + "/";
        public const string ConditionStr = "Condition";
        public const string ConditionPrefix = ConditionStr + "/";
        public const string DeviceStr = "Device";
        public const string DevicePrefix = DeviceStr + "/";
        public const string DiagnosticReportStr = "DiagnosticReport";
        public const string DiagnosticReportPrefix = DiagnosticReportStr + "/";
        public const string EncounterStr = "Encounter";
        public const string EncounterPrefix = EncounterStr + "/";
        public const string ExplanationOfBenefitStr = "ExplanationOfBenefit";
        public const string ExplanationOfBenefitPrefix = ExplanationOfBenefitStr + "/";
        public const string ImagingStudyStr = "ImagingStudy";
        public const string ImagingStudyPrefix = ImagingStudyStr + "/";
        public const string ImmunizationStr = "Immunization";
        public const string ImmunizationPrefix = ImmunizationStr + "/";
        public const string MedicationAdministrationStr = "MedicationAdministration";
        public const string MedicationAdministrationPrefix = MedicationAdministrationStr + "/";
        public const string MedicationRequestStr = "MedicationRequest";
        public const string MedicationRequestPrefix = MedicationRequestStr + "/";
        public const string ObservationStr = "Observation";
        public const string ObservationPrefix = ObservationStr + "/";
        public const string OrganizationStr = "Organization";
        public const string OrganizationPrefix = OrganizationStr + "/";
        public const string PatientStr = "Patient";
        public const string PatientPrefix = PatientStr + "/";
        public const string PractitionerStr = "Practitioner";
        public const string PractitionerPrefix = PractitionerStr + "/";
        public const string ProcedureStr = "Procedure";
        public const string ProcedurePrefix = ProcedureStr + "/";
        public const string SupplyDeliveryStr = "SupplyDelivery";
        public const string SupplyDeliveryPrefix = SupplyDeliveryStr + "/";

        public const string DocumentReferenceStr = "DocumentReference";
        public const string DocumentReferencePrefix = DocumentReferenceStr + "/";
        public const string StructureDefinitionStr = "StructureDefinition";
        public const string StructureDefinitionPrefix = StructureDefinitionStr + "/";
        public const string CommunicationStr = "Communication";
        public const string CommunicationPrefix = CommunicationStr + "/";

        protected abstract bool OnlyVerifyInput { get; }

        public Dictionary<string, ResourceSiblingsContainer<AllergyIntoleranceSibling>> AllergyIntolerances { get; } = new Dictionary<string, ResourceSiblingsContainer<AllergyIntoleranceSibling>>();

        public HashSet<string> AllergyIntoleranceIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<CarePlanSibling>> CarePlans { get; } = new Dictionary<string, ResourceSiblingsContainer<CarePlanSibling>>();

        public HashSet<string> CarePlanIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<CareTeamSibling>> CareTeams { get; } = new Dictionary<string, ResourceSiblingsContainer<CareTeamSibling>>();

        public HashSet<string> CareTeamIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ClaimSibling>> Claims { get; } = new Dictionary<string, ResourceSiblingsContainer<ClaimSibling>>();

        public HashSet<string> ClaimIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ConditionSibling>> Conditions { get; } = new Dictionary<string, ResourceSiblingsContainer<ConditionSibling>>();

        public HashSet<string> ConditionIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<DeviceSibling>> Devices { get; } = new Dictionary<string, ResourceSiblingsContainer<DeviceSibling>>();

        public HashSet<string> DeviceIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<DiagnosticReportSibling>> DiagnosticReports { get; } = new Dictionary<string, ResourceSiblingsContainer<DiagnosticReportSibling>>();

        public HashSet<string> DiagnosticReportIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<EncounterSibling>> Encounters { get; } = new Dictionary<string, ResourceSiblingsContainer<EncounterSibling>>();

        public HashSet<string> EncounterIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ExplanationOfBenefitSibling>> ExplanationOfBenefits { get; } = new Dictionary<string, ResourceSiblingsContainer<ExplanationOfBenefitSibling>>();

        public HashSet<string> ExplanationOfBenefitIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ImagingStudySibling>> ImagingStudies { get; } = new Dictionary<string, ResourceSiblingsContainer<ImagingStudySibling>>();

        public HashSet<string> ImagingStudyIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ImmunizationSibling>> Immunizations { get; } = new Dictionary<string, ResourceSiblingsContainer<ImmunizationSibling>>();

        public HashSet<string> ImmunizationIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<MedicationAdministrationSibling>> MedicationAdministrations { get; } = new Dictionary<string, ResourceSiblingsContainer<MedicationAdministrationSibling>>();

        public HashSet<string> MedicationAdministrationIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<MedicationRequestSibling>> MedicationRequests { get; } = new Dictionary<string, ResourceSiblingsContainer<MedicationRequestSibling>>();

        public HashSet<string> MedicationRequestIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ObservationSibling>> Observations { get; } = new Dictionary<string, ResourceSiblingsContainer<ObservationSibling>>();

        public HashSet<string> ObservationIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<OrganizationSibling>> Organizations { get; } = new Dictionary<string, ResourceSiblingsContainer<OrganizationSibling>>();

        public HashSet<string> OrganizationIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<PatientSibling>> Patients { get; } = new Dictionary<string, ResourceSiblingsContainer<PatientSibling>>();

        public HashSet<string> PatientIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<PractitionerSibling>> Practitioners { get; } = new Dictionary<string, ResourceSiblingsContainer<PractitionerSibling>>();

        public HashSet<string> PractitionerIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<ProcedureSibling>> Procedures { get; } = new Dictionary<string, ResourceSiblingsContainer<ProcedureSibling>>();

        public HashSet<string> ProcedureIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<SupplyDeliverySibling>> SupplyDeliveries { get; } = new Dictionary<string, ResourceSiblingsContainer<SupplyDeliverySibling>>();

        public HashSet<string> SupplyDeliveryIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>> DocumentReferences { get; } = new Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>>();

        public HashSet<string> DocumentReferenceIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>> StructureDefinitions { get; } = new Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>>();

        public HashSet<string> StructureDefinitionIdsRemoved { get; } = new HashSet<string>();

        public Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>> Communications { get; } = new Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>>();

        public HashSet<string> CommunicationIdsRemoved { get; } = new HashSet<string>();

        public abstract void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message);

        public abstract void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message);

        protected abstract Task MakeOutputResourceGroupDirAsync();

        public abstract string GetResourceGroupDir();

        protected abstract Task<StreamReader> GetStreamReader(string resourceName);

        protected abstract Task<StreamWriter> GetStreamWriter(string resourceName);

        private bool ValidateIdAndResourceType(string id, string resourceType, string resourceName, HashSet<string> duplicateIdsCheck)
        {
            if (id == null)
            {
                LogWarning(GetResourceGroupDir(), resourceName, id, $"Resource id is null!");
                return false;
            }

            if (resourceType != resourceName)
            {
                LogWarning(GetResourceGroupDir(), resourceName, id, $"Invalid resource type, expected {resourceName}, found {resourceType}!");
                duplicateIdsCheck.Add(id);
                return false;
            }

            if (duplicateIdsCheck.Contains(id))
            {
                // Repeat resurce id.
                if (resourceName != "Organization" && resourceName != "Practitioner") // TODO: We already know Organization and Practitioner resources have large number of repeat resources, no need to log.
                {
                    LogWarning(GetResourceGroupDir(), resourceName, id, $"Repeat resource id '{id}'!");
                }

                duplicateIdsCheck.Add(id);
                return false;
            }

            duplicateIdsCheck.Add(id);
            return true;
        }

        public bool ValidateResourceRefAndSelect<T>(
            string id,
            string resourceName,
            string resourceRef,
            string resourceRefName,
            Dictionary<string, ResourceSiblingsContainer<T>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            ref bool select)
            where T : struct
        {
            if (resourceRef == null)
            {
                LogWarning(GetResourceGroupDir(), resourceName, id, $"'{resourceRefName}' reference is null!");
                select = false;
                return false;
            }

            string prefix = $"{resourceRefName}/";
            if (!resourceRef.StartsWith(prefix, StringComparison.Ordinal))
            {
                LogWarning(GetResourceGroupDir(), resourceName, id, $"'{resourceRefName}' reference '{resourceRef}' does not start with '{prefix}'!");
                select = false;
                return false;
            }

            string resourceRefId = resourceRef.Substring(prefix.Length);
            if (resourcesCollection.ContainsKey(resourceRefId))
            {
                // select &= true;
                return true;
            }

            if (resourcesRemovedSet.Contains(resourceRefId))
            {
                select = false;
                return true;
            }
            else
            {
                LogWarning(GetResourceGroupDir(), resourceName, id, $"'{resourceRefName}' reference id {resourceRefId} in reference '{resourceRef}' does not match any '{resourceRefName}' ids!");
                select = false;
                return false;
            }
        }

        private async Task ResizeResources<T, TS>(
            string resourceName,
            TargetRatios.TargetProfile targetProfile,
            List<T> jsonList,
            StreamWriter w,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<TS>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            ResourceAdapter<T, TS> adapter,
            ResourcesResult result,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where TS : struct
        {
            if (targetProfile.ratios.ContainsKey(resourceName) && jsonList.Count > 0)
            {
                double ratio = targetProfile.ratios[resourceName] * ((double)result.InputResourcesCount / jsonList.Count);
                int requiredOutputCount = (int)(jsonList.Count * ratio);
                if (requiredOutputCount <= jsonList.Count)
                {
                    for (int i = 0; i < requiredOutputCount; i++)
                    {
                        T json = jsonList[i];
                        jsonList[i] = default(T);
                        ResourceSiblingsContainer<TS> siblingsContainer = new ResourceSiblingsContainer<TS>(new TS[1] { adapter.CreateOriginal(this, json) });
                        resourcesCollection.Add(adapter.GetId(json), siblingsContainer);
                        string line = JsonSerializer.Serialize<T>(json, options);
                        await w.WriteLineAsync(line);
                        result.OutputResourcesCount++;
                        result.OutputResourcesSize += line.Length;
                    }

                    for (int i = requiredOutputCount; i < jsonList.Count; i++)
                    {
                        T json = jsonList[i];
                        jsonList[i] = default(T);
                        resourcesRemovedSet.Add(adapter.GetId(json));
                        result.InputRemovedResourcesCount++;
                    }
                }
                else
                {
                    List<TS> siblingsList = new List<TS>(1000);
                    for (int i = 0; i < jsonList.Count; i++)
                    {
                        siblingsList.Clear(); // IMPORTANT, must be at the begining of the main loop!
                        T json = jsonList[i];
                        jsonList[i] = default(T);
                        siblingsList.Add(adapter.CreateOriginal(this, json));
                        string line = JsonSerializer.Serialize<T>(json, options);
                        await w.WriteLineAsync(line);
                        result.OutputResourcesCount++;
                        result.OutputResourcesSize += line.Length;

                        // Now generate more resources.
                        int requiredCount = (int)((i + 1) * ratio);
                        if (result.OutputResourcesCount < requiredCount)
                        {
                            T siblingJson = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize<T>(json));
                            int refSiblingNumberLimit = adapter.GetRefSiblingNumberLimit(this, json);
                            int refSiblingNumber = 1;
                            while (result.OutputResourcesCount < requiredCount)
                            {
                                siblingsList.Add(adapter.CreateClone(this, json, siblingJson, refSiblingNumber % refSiblingNumberLimit));
                                string siblingLine = JsonSerializer.Serialize<T>(siblingJson, options);
                                await w.WriteLineAsync(siblingLine);
                                result.OutputCreatedResourcesCount++;
                                result.OutputResourcesCount++;
                                result.OutputResourcesSize += siblingLine.Length;
                                refSiblingNumber++;
                            }
                        }

                        ResourceSiblingsContainer<TS> resourceAndClones = new ResourceSiblingsContainer<TS>(siblingsList.ToArray());
                        resourcesCollection.Add(adapter.GetId(json), resourceAndClones);
                    }
                }
            }

            if (result.OutputResourcesCount <= 0)
            {
                // TODO: what if no resources at this point (for example we need at least one patient), must retain at least one or even create one.
                LogWarning(GetResourceGroupDir(), resourceName, null, $"No resources of type '{resourceName}' in output!");
            }

            ret.Add(resourceName, result);
        }

        private async Task ProcessResources<T, TS>(
            string resourceName,
            ResourceAdapter<T, TS> adapter,
            TargetRatios.TargetProfile targetProfile,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<TS>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where TS : struct
        {
            adapter.Initialize(this, options);
            if ((!targetProfile.ratios.TryGetValue(resourceName, out double ratio)) || (ratio <= 0))
            {
                return; // Nothing to do.
            }

            List<T> jsonList = new List<T>();
            ResourcesResult result = new ResourcesResult();
            using (StreamReader r = await GetStreamReader(resourceName))
            {
                string line;
                HashSet<string> duplicateIdsCheck = new HashSet<string>();
                while ((line = await r.ReadLineAsync()) != null) // TODO: optimization, we can stop the loop once required number of items is reached?
                {
                    result.InputResourcesCount++;
                    result.InputResourcesSize += line.Length;
                    T json = JsonSerializer.Deserialize<T>(line);

                    if (!ValidateIdAndResourceType(adapter.GetId(json), adapter.GetResourceType(json), resourceName, duplicateIdsCheck))
                    {
                        continue;
                    }

                    if (!adapter.ValidateResourceRefsAndSelect(this, json, out bool select))
                    {
                        continue;
                    }

                    result.InputValidResorcesCount++;
                    if (OnlyVerifyInput || !select)
                    {
                        resourcesRemovedSet.Add(adapter.GetId(json));
                    }
                    else
                    {
                        result.InputSelectedResorcesCount++;
                        jsonList.Add(json);
                    }
                }
            }

            if (OnlyVerifyInput)
            {
                result.OutputResourcesCount = result.InputResourcesCount;
                result.OutputResourcesSize = result.InputResourcesSize;
                ret.Add(resourceName, result);
            }
            else
            {
                using (StreamWriter w = await GetStreamWriter(resourceName))
                {
                    await ResizeResources(resourceName, targetProfile, jsonList, w, options, resourcesCollection, resourcesRemovedSet, adapter, result, ret);
                }
            }
        }

        private async Task ProcessResourcesX<T, TS>(
            string resourceName,
            ResourceAdapter<T, TS> adapter,
            TargetRatios.TargetProfile targetProfile,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<TS>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where TS : struct
        {
            adapter.Initialize(this, options);
            if (OnlyVerifyInput)
            {
                await ProcessResources(resourceName, adapter, targetProfile, options, resourcesCollection, resourcesRemovedSet, ret);
                return;
            }

            if ((!targetProfile.ratios.TryGetValue(resourceName, out double ratio)) || (ratio <= 0))
            {
                return; // Nothing to do.
            }

            List<T> jsonList = new List<T>();
            ResourcesResult result = new ResourcesResult();
            {
                HashSet<string> duplicateIdsCheck = new HashSet<string>();
                foreach (ResourceAdapter<T, TS>.EnumeratorItem item in adapter)
                {
                    result.InputResourcesSize += item.Size;
                    if (!ValidateIdAndResourceType(adapter.GetId(item.Json), adapter.GetResourceType(item.Json), resourceName, duplicateIdsCheck))
                    {
                        continue;
                    }

                    if (!adapter.ValidateResourceRefsAndSelect(this, item.Json, out bool select))
                    {
                        continue;
                    }

                    result.InputValidResorcesCount++;

                    if (OnlyVerifyInput || !select)
                    {
                        resourcesRemovedSet.Add(adapter.GetId(item.Json));
                    }
                    else
                    {
                        result.InputSelectedResorcesCount++;
                        jsonList.Add(item.Json);
                    }
                }

                result.InputResourcesCount = 1;
            }

            if (OnlyVerifyInput)
            {
                result.OutputResourcesCount = result.InputResourcesCount;
                result.OutputResourcesSize = result.InputResourcesSize;
                ret.Add(resourceName, result);
            }
            else
            {
                using (StreamWriter w = await GetStreamWriter(resourceName))
                {
                    await ResizeResources(resourceName, targetProfile, jsonList, w, options, resourcesCollection, resourcesRemovedSet, adapter, result, ret);
                }
            }
        }

        public async Task<Dictionary<string, ResourcesResult>> ProcessResourceGroupAsync(TargetRatios.TargetProfile targetProfile)
        {
            await MakeOutputResourceGroupDirAsync();
            JsonSerializerOptions options = new()
            {
                // Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,// DANGEROUS!, use carefully if needed.
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            Dictionary<string, ResourcesResult> ret = new Dictionary<string, ResourcesResult>();

            await ProcessResourcesX(StructureDefinitionStr, new StructureDefinitionAdapter(), targetProfile, options, StructureDefinitions, StructureDefinitionIdsRemoved, ret);

            await ProcessResources(PatientStr, new PatientAdapter(), targetProfile, options, Patients, PatientIdsRemoved, ret);

            await ProcessResourcesX(CommunicationStr, new CommunicationAdapter(), targetProfile, options, Communications, CommunicationIdsRemoved, ret);

            await ProcessResources(AllergyIntoleranceStr, new AllergyIntoleranceAdapter(), targetProfile, options, AllergyIntolerances, AllergyIntoleranceIdsRemoved, ret);
            await ProcessResources(DeviceStr, new DeviceAdapter(), targetProfile, options, Devices, DeviceIdsRemoved, ret);
            await ProcessResources(SupplyDeliveryStr, new SupplyDeliveryAdapter(), targetProfile, options, SupplyDeliveries, SupplyDeliveryIdsRemoved, ret);

            await ProcessResources(OrganizationStr, new OrganizationAdapter(), targetProfile, options, Organizations, OrganizationIdsRemoved, ret);
            await ProcessResources(PractitionerStr, new PractitionerAdapter(), targetProfile, options, Practitioners, PractitionerIdsRemoved, ret);
            await ProcessResources(EncounterStr, new EncounterAdapter(), targetProfile, options, Encounters, EncounterIdsRemoved, ret);

            await ProcessResourcesX(DocumentReferenceStr, new DocumentReferenceAdapter(), targetProfile, options, DocumentReferences, DocumentReferenceIdsRemoved, ret);

            await ProcessResources(CareTeamStr, new CareTeamAdapter(), targetProfile, options, CareTeams, CareTeamIdsRemoved, ret);
            await ProcessResources(CarePlanStr, new CarePlanAdapter(), targetProfile, options, CarePlans, CarePlanIdsRemoved, ret);
            await ProcessResources(MedicationRequestStr, new MedicationRequestAdapter(), targetProfile, options, MedicationRequests, MedicationRequestIdsRemoved, ret);
            await ProcessResources(ClaimStr, new ClaimAdapter(), targetProfile, options, Claims, ClaimIdsRemoved, ret);

            await ProcessResources(ConditionStr, new ConditionAdapter(), targetProfile, options, Conditions, ConditionIdsRemoved, ret);
            await ProcessResources(ImagingStudyStr, new ImagingStudyAdapter(), targetProfile, options, ImagingStudies, ImagingStudyIdsRemoved, ret);
            await ProcessResources(ImmunizationStr, new ImmunizationAdapter(), targetProfile, options, Immunizations, ImmunizationIdsRemoved, ret);
            await ProcessResources(MedicationAdministrationStr, new MedicationAdministrationAdapter(), targetProfile, options, MedicationAdministrations, MedicationAdministrationIdsRemoved, ret);
            await ProcessResources(ProcedureStr, new ProcedureAdapter(), targetProfile, options, Procedures, ProcedureIdsRemoved, ret);
            await ProcessResources(ObservationStr, new ObservationAdapter(), targetProfile, options, Observations, ObservationIdsRemoved, ret);
            await ProcessResources(DiagnosticReportStr, new DiagnosticReportAdapter(), targetProfile, options, DiagnosticReports, DiagnosticReportIdsRemoved, ret);
            await ProcessResources(ExplanationOfBenefitStr, new ExplanationOfBenefitAdapter(), targetProfile, options, ExplanationOfBenefits, ExplanationOfBenefitIdsRemoved, ret);

            return ret;
        }

        public class ResourcesResult
        {
            public ResourcesResult()
            {
                InputResourcesCount = 0;
                InputResourcesSize = 0;
                InputValidResorcesCount = 0;
                InputSelectedResorcesCount = 0;
                InputRemovedResourcesCount = 0;
                OutputCreatedResourcesCount = 0;
                OutputResourcesCount = 0;
                OutputResourcesSize = 0;
            }

            public int InputResourcesCount { get; set; }

            public long InputResourcesSize { get; set; }

            public int InputValidResorcesCount { get; set; }

            public int InputSelectedResorcesCount { get; set; }

            public int InputRemovedResourcesCount { get; set; }

            public int OutputCreatedResourcesCount { get; set; }

            public int OutputResourcesCount { get; set; }

            public long OutputResourcesSize { get; set; }

            public void Add(ResourcesResult a) // TODO: to derived class so base is readonly?
            {
                InputResourcesCount += a.InputResourcesCount;
                InputResourcesSize += a.InputResourcesSize;
                InputValidResorcesCount += a.InputValidResorcesCount;
                InputSelectedResorcesCount += a.InputSelectedResorcesCount;
                InputRemovedResourcesCount += a.InputRemovedResourcesCount;
                OutputCreatedResourcesCount += a.OutputCreatedResourcesCount;
                OutputResourcesCount += a.OutputResourcesCount;
                OutputResourcesSize += a.OutputResourcesSize;
            }

            public void LogInfo(ResourceGroupProcessor resourceGroupProcessor, string resourceGroupDir, string resourceName)
            {
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, "-----------------------------------------------");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(InputResourcesCount)} : {InputResourcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(InputResourcesSize)} : {InputResourcesSize}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(InputValidResorcesCount)} : {InputValidResorcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(InputSelectedResorcesCount)} : {InputSelectedResorcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(InputRemovedResourcesCount)} : {InputRemovedResourcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(OutputCreatedResourcesCount)} : {OutputCreatedResourcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(OutputResourcesCount)} : {OutputResourcesCount}");
                resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(OutputResourcesSize)} : {OutputResourcesSize}");
            }
        }
    }
}
