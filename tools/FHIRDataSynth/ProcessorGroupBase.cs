using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ResourceProcessorNamespace
{
    internal class ResourcesResult
    {
        public int inputResourcesCount;
        public long inputResourcesSize;
        public int inputValidResorcesCount;
        public int inputSelectedResorcesCount;
        public int inputRemovedResourcesCount;
        public int outputCreatedResourcesCount;
        public int outputResourcesCount;
        public long outputResourcesSize;

        public int InputResourcesCount { get => inputResourcesCount; }

        public long InputResourcesSize { get => inputResourcesSize; }

        public int InputValidResorcesCount { get => inputValidResorcesCount; }

        public int InputSelectedResorcesCount { get => inputSelectedResorcesCount; }

        public int InputRemovedResourcesCount { get => inputRemovedResourcesCount; }

        public int OutputCreatedResourcesCount { get => outputCreatedResourcesCount; }

        public int OutputResourcesCount { get => outputResourcesCount; }

        public long OutputResourcesSize { get => outputResourcesSize; }

        public ResourcesResult(string resourceName)
        {
            inputResourcesCount = 0;
            inputResourcesSize = 0;
            inputValidResorcesCount = 0;
            inputSelectedResorcesCount = 0;
            inputRemovedResourcesCount = 0;
            outputCreatedResourcesCount = 0;
            outputResourcesCount = 0;
            outputResourcesSize = 0;
        }

        public ResourcesResult(
            int inputResourcesCount,
            long inputResourcesSize,
            int inputValidResorcesCount,
            int inputSelectedResorcesCount,
            int inputRemovedResourcesCount,
            int outputCreatedResourcesCount,
            int outputResourcesCount,
            long outputResourcesSize)
        {
            this.inputResourcesCount = inputResourcesCount;
            this.inputResourcesSize = inputResourcesSize;
            this.inputValidResorcesCount = inputValidResorcesCount;
            this.inputSelectedResorcesCount = inputSelectedResorcesCount;
            this.inputRemovedResourcesCount = inputRemovedResourcesCount;
            this.outputCreatedResourcesCount = outputCreatedResourcesCount;
            this.outputResourcesCount = outputResourcesCount;
            this.outputResourcesSize = outputResourcesSize;
        }

        public void Add(ResourcesResult a) // TODO: to derived class so base is readonly?
        {
            inputResourcesCount += a.inputResourcesCount;
            inputResourcesSize += a.inputResourcesSize;
            inputValidResorcesCount += a.inputValidResorcesCount;
            inputSelectedResorcesCount += a.inputSelectedResorcesCount;
            inputRemovedResourcesCount += a.inputRemovedResourcesCount;
            outputCreatedResourcesCount += a.outputCreatedResourcesCount;
            outputResourcesCount += a.outputResourcesCount;
            outputResourcesSize += a.outputResourcesSize;
        }

        public void LogInfo(ResourceGroupProcessor resourceGroupProcessor, string resourceGroupDir, string resourceName)
        {
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, "-----------------------------------------------");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(inputResourcesCount)} : {inputResourcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(inputResourcesSize)} : {inputResourcesSize}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(inputValidResorcesCount)} : {inputValidResorcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(inputSelectedResorcesCount)} : {inputSelectedResorcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(inputRemovedResourcesCount)} : {inputRemovedResourcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(outputCreatedResourcesCount)} : {outputCreatedResourcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(outputResourcesCount)} : {outputResourcesCount}");
            resourceGroupProcessor.LogInfo(resourceGroupDir, resourceName, null, $" {nameof(outputResourcesSize)} : {outputResourcesSize}");
        }
    }

    internal abstract class ResourceGroupProcessor
    {
        public abstract void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message);

        public abstract void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message);

        protected abstract Task MakeOutputResourceGroupDirAsync();

        public abstract string GetResourceGroupDir();

        protected abstract Task<StreamReader> GetStreamReader(string resourceName);

        protected abstract Task<StreamWriter> GetStreamWriter(string resourceName);

        protected abstract bool OnlyVerifyInput { get; }

        public Dictionary<string, ResourceSiblingsContainer<AllergyIntoleranceSibling>> allergyIntolerances = new Dictionary<string, ResourceSiblingsContainer<AllergyIntoleranceSibling>>();
        public HashSet<string> allergyIntoleranceIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<CarePlanSibling>> carePlans = new Dictionary<string, ResourceSiblingsContainer<CarePlanSibling>>();
        public HashSet<string> carePlanIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<CareTeamSibling>> careTeams = new Dictionary<string, ResourceSiblingsContainer<CareTeamSibling>>();
        public HashSet<string> careTeamIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ClaimSibling>> claims = new Dictionary<string, ResourceSiblingsContainer<ClaimSibling>>();
        public HashSet<string> claimIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ConditionSibling>> conditions = new Dictionary<string, ResourceSiblingsContainer<ConditionSibling>>();
        public HashSet<string> conditionIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<DeviceSibling>> devices = new Dictionary<string, ResourceSiblingsContainer<DeviceSibling>>();
        public HashSet<string> deviceIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<DiagnosticReportSibling>> diagnosticReports = new Dictionary<string, ResourceSiblingsContainer<DiagnosticReportSibling>>();
        public HashSet<string> diagnosticReportIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<EncounterSibling>> encounters = new Dictionary<string, ResourceSiblingsContainer<EncounterSibling>>();
        public HashSet<string> encounterIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ExplanationOfBenefitSibling>> explanationOfBenefits = new Dictionary<string, ResourceSiblingsContainer<ExplanationOfBenefitSibling>>();
        public HashSet<string> explanationOfBenefitIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ImagingStudySibling>> imagingStudies = new Dictionary<string, ResourceSiblingsContainer<ImagingStudySibling>>();
        public HashSet<string> imagingStudyIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ImmunizationSibling>> immunizations = new Dictionary<string, ResourceSiblingsContainer<ImmunizationSibling>>();
        public HashSet<string> immunizationIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<MedicationAdministrationSibling>> medicationAdministrations = new Dictionary<string, ResourceSiblingsContainer<MedicationAdministrationSibling>>();
        public HashSet<string> medicationAdministrationIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<MedicationRequestSibling>> medicationRequests = new Dictionary<string, ResourceSiblingsContainer<MedicationRequestSibling>>();
        public HashSet<string> medicationRequestIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ObservationSibling>> observations = new Dictionary<string, ResourceSiblingsContainer<ObservationSibling>>();
        public HashSet<string> observationIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<OrganizationSibling>> organizations = new Dictionary<string, ResourceSiblingsContainer<OrganizationSibling>>();
        public HashSet<string> organizationIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<PatientSibling>> patients = new Dictionary<string, ResourceSiblingsContainer<PatientSibling>>();
        public HashSet<string> patientIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<PractitionerSibling>> practitioners = new Dictionary<string, ResourceSiblingsContainer<PractitionerSibling>>();
        public HashSet<string> practitionerIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<ProcedureSibling>> procedures = new Dictionary<string, ResourceSiblingsContainer<ProcedureSibling>>();
        public HashSet<string> procedureIdsRemoved = new HashSet<string>();
        public Dictionary<string, ResourceSiblingsContainer<SupplyDeliverySibling>> supplyDeliveries = new Dictionary<string, ResourceSiblingsContainer<SupplyDeliverySibling>>();
        public HashSet<string> supplyDeliveryIdsRemoved = new HashSet<string>();

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
        public Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>> documentReferences = new Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>>();
        public HashSet<string> documentReferenceIdsRemoved = new HashSet<string>();

        public const string StructureDefinitionStr = "StructureDefinition";
        public const string StructureDefinitionPrefix = StructureDefinitionStr + "/";
        public Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>> structureDefinitions = new Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>>();
        public HashSet<string> structureDefinitionIdsRemoved = new HashSet<string>();

        public const string CommunicationStr = "Communication";
        public const string CommunicationPrefix = CommunicationStr + "/";
        public Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>> communications = new Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>>();
        public HashSet<string> communicationIdsRemoved = new HashSet<string>();

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
            if (!resourceRef.StartsWith(prefix))
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
            TargetProfile targetProfile,
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
                double ratio = targetProfile.ratios[resourceName] * ((double)result.inputResourcesCount / jsonList.Count);
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
                        result.outputResourcesCount++;
                        result.outputResourcesSize += line.Length;
                    }

                    for (int i = requiredOutputCount; i < jsonList.Count; i++)
                    {
                        T json = jsonList[i];
                        jsonList[i] = default(T);
                        resourcesRemovedSet.Add(adapter.GetId(json));
                        result.inputRemovedResourcesCount++;
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
                        result.outputResourcesCount++;
                        result.outputResourcesSize += line.Length;

                        // Now generate more resources.
                        int requiredCount = (int)((i + 1) * ratio);
                        if (result.outputResourcesCount < requiredCount)
                        {
                            T siblingJson = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize<T>(json));
                            int refSiblingNumberLimit = adapter.GetRefSiblingNumberLimit(this, json);
                            int refSiblingNumber = 1;
                            while (result.outputResourcesCount < requiredCount)
                            {
                                siblingsList.Add(adapter.CreateClone(this, json, siblingJson, refSiblingNumber % refSiblingNumberLimit));
                                string siblingLine = JsonSerializer.Serialize<T>(siblingJson, options);
                                await w.WriteLineAsync(siblingLine);
                                result.outputCreatedResourcesCount++;
                                result.outputResourcesCount++;
                                result.outputResourcesSize += siblingLine.Length;
                                refSiblingNumber++;
                            }
                        }

                        ResourceSiblingsContainer<TS> resourceAndClones = new ResourceSiblingsContainer<TS>(siblingsList.ToArray());
                        resourcesCollection.Add(adapter.GetId(json), resourceAndClones);
                    }
                }
            }

            if (result.outputResourcesCount <= 0)
            {
                // TODO: what if no resources at this point (for example we need at least one patient), must retain at least one or even create one.
                LogWarning(GetResourceGroupDir(), resourceName, null, $"No resources of type '{resourceName}' in output!");
            }

            ret.Add(resourceName, result);
        }

        private async Task ProcessResources<T, TS>(
            string resourceName,
            ResourceAdapter<T, TS> adapter,
            TargetProfile targetProfile,
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
            ResourcesResult result = new ResourcesResult(resourceName);
            using (StreamReader r = await GetStreamReader(resourceName))
            {
                string line;
                HashSet<string> duplicateIdsCheck = new HashSet<string>();
                while ((line = await r.ReadLineAsync()) != null) // TODO: optimization, we can stop the loop once required number of items is reached?
                {
                    result.inputResourcesCount++;
                    result.inputResourcesSize += line.Length;
                    T json = JsonSerializer.Deserialize<T>(line);

                    if (!ValidateIdAndResourceType(adapter.GetId(json), adapter.GetResourceType(json), resourceName, duplicateIdsCheck))
                    {
                        continue;
                    }

                    if (!adapter.ValidateResourceRefsAndSelect(this, json, out bool select))
                    {
                        continue;
                    }

                    result.inputValidResorcesCount++;
                    if (OnlyVerifyInput || !select)
                    {
                        resourcesRemovedSet.Add(adapter.GetId(json));
                    }
                    else
                    {
                        result.inputSelectedResorcesCount++;
                        jsonList.Add(json);
                    }
                }
            }

            if (OnlyVerifyInput)
            {
                result.outputResourcesCount = result.inputResourcesCount;
                result.outputResourcesSize = result.inputResourcesSize;
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
            TargetProfile targetProfile,
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
            ResourcesResult result = new ResourcesResult(resourceName);
            {
                HashSet<string> duplicateIdsCheck = new HashSet<string>();
                foreach (ResourceAdapter<T, TS>.EnumeratorItem item in adapter)
                {
                    result.inputResourcesSize += item.size;
                    if (!ValidateIdAndResourceType(adapter.GetId(item.json), adapter.GetResourceType(item.json), resourceName, duplicateIdsCheck))
                    {
                        continue;
                    }

                    if (!adapter.ValidateResourceRefsAndSelect(this, item.json, out bool select))
                    {
                        continue;
                    }

                    result.inputValidResorcesCount++;

                    if (OnlyVerifyInput || !select)
                    {
                        resourcesRemovedSet.Add(adapter.GetId(item.json));
                    }
                    else
                    {
                        result.inputSelectedResorcesCount++;
                        jsonList.Add(item.json);
                    }
                }

                result.inputResourcesCount = 1;
            }

            if (OnlyVerifyInput)
            {
                result.outputResourcesCount = result.inputResourcesCount;
                result.outputResourcesSize = result.inputResourcesSize;
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

        public async Task<Dictionary<string, ResourcesResult>> ProcessResourceGroupAsync(TargetProfile targetProfile)
        {
            await MakeOutputResourceGroupDirAsync();
            JsonSerializerOptions options = new()
            {
                // Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,// DANGEROUS!, use carefully if needed.
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            Dictionary<string, ResourcesResult> ret = new Dictionary<string, ResourcesResult>();

            await ProcessResourcesX(StructureDefinitionStr, new StructureDefinitionAdapter(), targetProfile, options, structureDefinitions, structureDefinitionIdsRemoved, ret);

            await ProcessResources(PatientStr, new PatientAdapter(), targetProfile, options, patients, patientIdsRemoved, ret);

            await ProcessResourcesX(CommunicationStr, new CommunicationAdapter(), targetProfile, options, communications, communicationIdsRemoved, ret);

            await ProcessResources(AllergyIntoleranceStr, new AllergyIntoleranceAdapter(), targetProfile, options, allergyIntolerances, allergyIntoleranceIdsRemoved, ret);
            await ProcessResources(DeviceStr, new DeviceAdapter(), targetProfile, options, devices, deviceIdsRemoved, ret);
            await ProcessResources(SupplyDeliveryStr, new SupplyDeliveryAdapter(), targetProfile, options, supplyDeliveries, supplyDeliveryIdsRemoved, ret);

            await ProcessResources(OrganizationStr, new OrganizationAdapter(), targetProfile, options, organizations, organizationIdsRemoved, ret);
            await ProcessResources(PractitionerStr, new PractitionerAdapter(), targetProfile, options, practitioners, practitionerIdsRemoved, ret);
            await ProcessResources(EncounterStr, new EncounterAdapter(), targetProfile, options, encounters, encounterIdsRemoved, ret);

            await ProcessResourcesX(DocumentReferenceStr, new DocumentReferenceAdapter(), targetProfile, options, documentReferences, documentReferenceIdsRemoved, ret);

            await ProcessResources(CareTeamStr, new CareTeamAdapter(), targetProfile, options, careTeams, careTeamIdsRemoved, ret);
            await ProcessResources(CarePlanStr, new CarePlanAdapter(), targetProfile, options, carePlans, carePlanIdsRemoved, ret);
            await ProcessResources(MedicationRequestStr, new MedicationRequestAdapter(), targetProfile, options, medicationRequests, medicationRequestIdsRemoved, ret);
            await ProcessResources(ClaimStr, new ClaimAdapter(), targetProfile, options, claims, claimIdsRemoved, ret);

            await ProcessResources(ConditionStr, new ConditionAdapter(), targetProfile, options, conditions, conditionIdsRemoved, ret);
            await ProcessResources(ImagingStudyStr, new ImagingStudyAdapter(), targetProfile, options, imagingStudies, imagingStudyIdsRemoved, ret);
            await ProcessResources(ImmunizationStr, new ImmunizationAdapter(), targetProfile, options, immunizations, immunizationIdsRemoved, ret);
            await ProcessResources(MedicationAdministrationStr, new MedicationAdministrationAdapter(), targetProfile, options, medicationAdministrations, medicationAdministrationIdsRemoved, ret);
            await ProcessResources(ProcedureStr, new ProcedureAdapter(), targetProfile, options, procedures, procedureIdsRemoved, ret);
            await ProcessResources(ObservationStr, new ObservationAdapter(), targetProfile, options, observations, observationIdsRemoved, ret);
            await ProcessResources(DiagnosticReportStr, new DiagnosticReportAdapter(), targetProfile, options, diagnosticReports, diagnosticReportIdsRemoved, ret);
            await ProcessResources(ExplanationOfBenefitStr, new ExplanationOfBenefitAdapter(), targetProfile, options, explanationOfBenefits, explanationOfBenefitIdsRemoved, ret);

            return ret;
        }
    }
}
