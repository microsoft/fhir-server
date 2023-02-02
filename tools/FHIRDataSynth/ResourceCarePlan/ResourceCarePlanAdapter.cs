// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourceCarePlanAdapter : ResourceAdapterBase<CarePlan.Rootobject, ResourceCarePlanAdapter.CarePlanSibling>
    {
        public override CarePlanSibling CreateOriginal(ResourceGroupProcessor processor, CarePlan.Rootobject json)
        {
            return default;
        }

        public override string GetId(CarePlan.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(CarePlan.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, CarePlan.Rootobject originalJson, CarePlan.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.careTeam != null)
            {
                for (int i = 0; i < cloneJson.careTeam.Length; i++)
                {
                    cloneJson.careTeam[i].reference = CloneOrLimit(clone, originalJson, originalJson.careTeam[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                }
            }
        }

        public override CarePlanSibling CreateClone(
            ResourceGroupProcessor processor,
            CarePlan.Rootobject originalJson, // WARNING! originalJson MUST not be modified, member classes of originalJson MUST not be asigned to cloneJson!
            CarePlan.Rootobject cloneJson,
            int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            /*
            string patientRef = null;
            string encounterRef = null;
            string careTeamRef = null;
            // Get references.
            if (originalJson.encounter?.reference != null)
            {
                string encounterId = originalJson.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                EncounterSibling encounter = processor.encounters[encounterId].Get(refSiblingNumber);
                encounterRef = ResourceGroupProcessor.encounterPrefix + encounter.id;
                patientRef = encounter.subjectRef;
            }
            if (patientRef == null)
            {
                string patientId = originalJson.subject.reference.Substring(ResourceGroupProcessor.patientPrefix.Length);
                PatientSibling patient = processor.patients[patientId].Get(refSiblingNumber);
                patientRef = ResourceGroupProcessor.patientPrefix + patient.id;
            }
            if (careTeamRef == null && originalJson.careTeam?[0].reference != null)// Already validated, if careTeam != null, must not be empty.
            {
                string careTeamId = originalJson.careTeam[0].reference.Substring(ResourceGroupProcessor.careTeamPrefix.Length);
                CareTeamSibling careTeam = processor.careTeams[careTeamId].Get(refSiblingNumber);
                careTeamRef = ResourceGroupProcessor.careTeamPrefix + careTeam.id;
            }
            // Have new references. Set new references in clone.
            cloneJson.id = Guid.NewGuid().ToString();
            if (cloneJson.subject?.reference != null)
            {
                if (patientRef != null)
                {
                    cloneJson.subject.reference = patientRef;
                }
                else
                {
                    cloneJson.subject = null;
                }
            }
            if (cloneJson.encounter?.reference != null)
            {
                if (encounterRef != null)
                {
                    cloneJson.encounter.reference = encounterRef;
                }
                else
                {
                    cloneJson.encounter = null;
                }
            }
            if (cloneJson.careTeam != null)
            {
                if (careTeamRef != null)
                {
                    CarePlan.Careteam cp = cloneJson.careTeam[0];// Already validated, if managingOrganization != null, must not be empty.
                    cp.reference = careTeamRef;
                    if (cloneJson.careTeam.Length != 1)
                    {
                        cloneJson.careTeam = new CarePlan.Careteam[1];
                        cloneJson.careTeam[0] = cp;
                    }
                }
                else
                {
                    cloneJson.careTeam = null;
                }
            }
            */
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, CarePlan.Rootobject json, out bool select)
        {
            string resName = ResourceGroupProcessor.CarePlanStr;
            select = true;

            if (json.subject == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'subject' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, resName, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.encounter != null && !processor.ValidateResourceRefAndSelect(json.id, resName, json.encounter.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.careTeam != null)
            {
                if (json.careTeam.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'careTeam' is empty!");
                    select = false;
                    return false;
                }

                foreach (CarePlan.Careteam ct in json.careTeam)
                {
                    if (!processor.ValidateResourceRefAndSelect(json.id, resName, ct.reference, ResourceGroupProcessor.CareTeamStr, processor.CareTeams, processor.CareTeamIdsRemoved, ref select))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            return true;
        }

        internal struct CarePlanSibling
        {
        }
    }
}
