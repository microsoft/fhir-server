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
    class ResourcesResult
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

    abstract class ResourceGroupProcessor
    {
        abstract public void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message);

        abstract public void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message);

        abstract protected Task MakeOutputResourceGroupDirAsync();

        abstract public string GetResourceGroupDir();

        abstract protected Task<StreamReader> GetStreamReader(string resourceName);

        abstract protected Task<StreamWriter> GetStreamWriter(string resourceName);

        abstract protected bool OnlyVerifyInput { get; }

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

        public const string allergyIntoleranceStr = "AllergyIntolerance";
        public const string allergyIntolerancePrefix = allergyIntoleranceStr + "/";
        public const string carePlanStr = "CarePlan";
        public const string carePlanPrefix = carePlanStr + "/";
        public const string careTeamStr = "CareTeam";
        public const string careTeamPrefix = careTeamStr + "/";
        public const string claimStr = "Claim";
        public const string claimPrefix = claimStr + "/";
        public const string conditionStr = "Condition";
        public const string conditionPrefix = conditionStr + "/";
        public const string deviceStr = "Device";
        public const string devicePrefix = deviceStr + "/";
        public const string diagnosticReportStr = "DiagnosticReport";
        public const string diagnosticReportPrefix = diagnosticReportStr + "/";
        public const string encounterStr = "Encounter";
        public const string encounterPrefix = encounterStr + "/";
        public const string explanationOfBenefitStr = "ExplanationOfBenefit";
        public const string explanationOfBenefitPrefix = explanationOfBenefitStr + "/";
        public const string imagingStudyStr = "ImagingStudy";
        public const string imagingStudyPrefix = imagingStudyStr + "/";
        public const string immunizationStr = "Immunization";
        public const string immunizationPrefix = immunizationStr + "/";
        public const string medicationAdministrationStr = "MedicationAdministration";
        public const string medicationAdministrationPrefix = medicationAdministrationStr + "/";
        public const string medicationRequestStr = "MedicationRequest";
        public const string medicationRequestPrefix = medicationRequestStr + "/";
        public const string observationStr = "Observation";
        public const string observationPrefix = observationStr + "/";
        public const string organizationStr = "Organization";
        public const string organizationPrefix = organizationStr + "/";
        public const string patientStr = "Patient";
        public const string patientPrefix = patientStr + "/";
        public const string practitionerStr = "Practitioner";
        public const string practitionerPrefix = practitionerStr + "/";
        public const string procedureStr = "Procedure";
        public const string procedurePrefix = procedureStr + "/";
        public const string supplyDeliveryStr = "SupplyDelivery";
        public const string supplyDeliveryPrefix = supplyDeliveryStr + "/";

        public const string documentReferenceStr = "DocumentReference";
        public const string documentReferencePrefix = documentReferenceStr + "/";
        public Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>> documentReferences = new Dictionary<string, ResourceSiblingsContainer<DocumentReferenceSibling>>();
        public HashSet<string> documentReferenceIdsRemoved = new HashSet<string>();

        public const string structureDefinitionStr = "StructureDefinition";
        public const string structureDefinitionPrefix = structureDefinitionStr + "/";
        public Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>> structureDefinitions = new Dictionary<string, ResourceSiblingsContainer<StructureDefinitionSibling>>();
        public HashSet<string> structureDefinitionIdsRemoved = new HashSet<string>();

        public const string communicationStr = "Communication";
        public const string communicationPrefix = communicationStr + "/";
        public Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>> communications = new Dictionary<string, ResourceSiblingsContainer<CommunicationSibling>>();
        public HashSet<string> communicationIdsRemoved = new HashSet<string>();

        bool ValidateIdAndResourceType(string id, string resourceType, string resourceName, HashSet<string> duplicateIdsCheck)
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

        private async Task ResizeResources<T, U>(
            string resourceName,
            TargetProfile targetProfile,
            List<T> jsonList,
            StreamWriter w,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<U>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            ResourceAdapter<T, U> adapter,
            ResourcesResult result,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where U : struct
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
                        ResourceSiblingsContainer<U> siblingsContainer = new ResourceSiblingsContainer<U>(new U[1] { adapter.CreateOriginal(this, json) });
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
                    List<U> siblingsList = new List<U>(1000);
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

                        ResourceSiblingsContainer<U> resourceAndClones = new ResourceSiblingsContainer<U>(siblingsList.ToArray());
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

        async Task ProcessResources<T, U>(
            string resourceName,
            ResourceAdapter<T, U> adapter,
            TargetProfile targetProfile,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<U>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where U : struct
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

        async Task ProcessResourcesX<T, U>(
            string resourceName,
            ResourceAdapter<T, U> adapter,
            TargetProfile targetProfile,
            JsonSerializerOptions options,
            Dictionary<string, ResourceSiblingsContainer<U>> resourcesCollection,
            HashSet<string> resourcesRemovedSet,
            Dictionary<string, ResourcesResult> ret)
            where T : class
            where U : struct
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
                foreach (ResourceAdapter<T, U>.EnumeratorItem item in adapter)
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

            await ProcessResourcesX(structureDefinitionStr, new StructureDefinitionAdapter(), targetProfile, options, structureDefinitions, structureDefinitionIdsRemoved, ret);

            await ProcessResources(patientStr, new PatientAdapter(), targetProfile, options, patients, patientIdsRemoved, ret);

            await ProcessResourcesX(communicationStr, new CommunicationAdapter(), targetProfile, options, communications, communicationIdsRemoved, ret);

            await ProcessResources(allergyIntoleranceStr, new AllergyIntoleranceAdapter(), targetProfile, options, allergyIntolerances, allergyIntoleranceIdsRemoved, ret);
            await ProcessResources(deviceStr, new DeviceAdapter(), targetProfile, options, devices, deviceIdsRemoved, ret);
            await ProcessResources(supplyDeliveryStr, new SupplyDeliveryAdapter(), targetProfile, options, supplyDeliveries, supplyDeliveryIdsRemoved, ret);

            await ProcessResources(organizationStr, new OrganizationAdapter(), targetProfile, options, organizations, organizationIdsRemoved, ret);
            await ProcessResources(practitionerStr, new PractitionerAdapter(), targetProfile, options, practitioners, practitionerIdsRemoved, ret);
            await ProcessResources(encounterStr, new EncounterAdapter(), targetProfile, options, encounters, encounterIdsRemoved, ret);

            await ProcessResourcesX(documentReferenceStr, new DocumentReferenceAdapter(), targetProfile, options, documentReferences, documentReferenceIdsRemoved, ret);

            await ProcessResources(careTeamStr, new CareTeamAdapter(), targetProfile, options, careTeams, careTeamIdsRemoved, ret);
            await ProcessResources(carePlanStr, new CarePlanAdapter(), targetProfile, options, carePlans, carePlanIdsRemoved, ret);
            await ProcessResources(medicationRequestStr, new MedicationRequestAdapter(), targetProfile, options, medicationRequests, medicationRequestIdsRemoved, ret);
            await ProcessResources(claimStr, new ClaimAdapter(), targetProfile, options, claims, claimIdsRemoved, ret);

            await ProcessResources(conditionStr, new ConditionAdapter(), targetProfile, options, conditions, conditionIdsRemoved, ret);
            await ProcessResources(imagingStudyStr, new ImagingStudyAdapter(), targetProfile, options, imagingStudies, imagingStudyIdsRemoved, ret);
            await ProcessResources(immunizationStr, new ImmunizationAdapter(), targetProfile, options, immunizations, immunizationIdsRemoved, ret);
            await ProcessResources(medicationAdministrationStr, new MedicationAdministrationAdapter(), targetProfile, options, medicationAdministrations, medicationAdministrationIdsRemoved, ret);
            await ProcessResources(procedureStr, new ProcedureAdapter(), targetProfile, options, procedures, procedureIdsRemoved, ret);
            await ProcessResources(observationStr, new ObservationAdapter(), targetProfile, options, observations, observationIdsRemoved, ret);
            await ProcessResources(diagnosticReportStr, new DiagnosticReportAdapter(), targetProfile, options, diagnosticReports, diagnosticReportIdsRemoved, ret);
            await ProcessResources(explanationOfBenefitStr, new ExplanationOfBenefitAdapter(), targetProfile, options, explanationOfBenefits, explanationOfBenefitIdsRemoved, ret);

            return ret;
        }
    }
}
