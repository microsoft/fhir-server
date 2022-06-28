// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal class ResourceExplanationOfBenefitAdapter : ResourceAdapterBase<ExplanationOfBenefit.Rootobject, ResourceExplanationOfBenefitAdapter.ExplanationOfBenefitSibling>
    {
        public override ExplanationOfBenefitSibling CreateOriginal(ResourceGroupProcessor processor, ExplanationOfBenefit.Rootobject json)
        {
            return default;
        }

        public override string GetId(ExplanationOfBenefit.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(ExplanationOfBenefit.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, ExplanationOfBenefit.Rootobject originalJson, ExplanationOfBenefit.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.contained != null)
            {
                for (int i = 0; i < cloneJson.contained.Length; i++)
                {
                    ExplanationOfBenefit.Contained c = cloneJson.contained[i];
                    if (c.resourceType == "ServiceRequest")
                    {
                        c.subject.reference = CloneOrLimit(clone, originalJson, originalJson.contained[i].subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
                        if (c.requester != null)
                        {
                            c.requester.reference = CloneOrLimit(clone, originalJson, originalJson.contained[i].requester.reference, refSiblingNumber, ref refSiblingNumberLimit);
                        }

                        if (c.performer != null)
                        {
                            for (int j = 0; j < c.performer.Length; j++)
                            {
                                c.performer[j].reference = CloneOrLimit(clone, originalJson, originalJson.contained[i].performer[j].reference, refSiblingNumber, ref refSiblingNumberLimit);
                            }
                        }
                    }
                    else if (c.resourceType == "Coverage")
                    {
                        c.beneficiary.reference = CloneOrLimit(clone, originalJson, originalJson.contained[i].beneficiary.reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }
            }

            cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
            cloneJson.provider.reference = CloneOrLimit(clone, originalJson, originalJson.provider.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.claim != null)
            {
                cloneJson.claim.reference = CloneOrLimit(clone, originalJson, originalJson.claim.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.careTeam != null)
            {
                for (int i = 0; i < cloneJson.careTeam.Length; i++)
                {
                    cloneJson.careTeam[i].provider.reference = CloneOrLimit(clone, originalJson, originalJson.careTeam[i].provider.reference, refSiblingNumber, ref refSiblingNumberLimit);
                }
            }

            if (cloneJson.item != null)
            {
                for (int i = 0; i < cloneJson.item.Length; i++)
                {
                    ExplanationOfBenefit.Item item = cloneJson.item[i];
                    if (item.encounter != null)
                    {
                        for (int j = 0; j < item.encounter.Length; j++)
                        {
                            item.encounter[j].reference = CloneOrLimit(clone, originalJson, originalJson.item[i].encounter[j].reference, refSiblingNumber, ref refSiblingNumberLimit);
                        }
                    }
                }
            }
        }

        public override ExplanationOfBenefitSibling CreateClone(ResourceGroupProcessor processor, ExplanationOfBenefit.Rootobject originalJson, ExplanationOfBenefit.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        /*public override ExplanationOfBenefitSibling CreateClone(
            ResourceGroupProcessor processor,
            // WARNING! originalJson MUST not be modified, member classes of originalJson MUST not be asigned to cloneJson!
            ExplanationOfBenefit.Rootobject originalJson,
            ExplanationOfBenefit.Rootobject cloneJson,
            int refSiblingNumber)
        {
            string patientRef = null;
            string practitionerRef = null;
            string claimRef = null;
            string encounterRef = null;
            // First try to get refs from claim.
            if (originalJson.claim != null)
            {
                string claimId = originalJson.claim.reference.Substring(ResourceGroupProcessor.claimPrefix.Length);
                ClaimSibling claim = processor.claims[claimId].Get(refSiblingNumber);
                claimRef = ResourceGroupProcessor.claimPrefix + claim.id;
                encounterRef = ResourceGroupProcessor.encounterPrefix + claim.encounter.id;
                patientRef = claim.encounter.subjectRef;
                practitionerRef = claim.encounter.participantRef;
            }
            else if (originalJson.item != null)
            {
                foreach (ExplanationOfBenefit.Item i in originalJson.item)
                {
                    if (i.encounter != null)
                    {
                        string encounterId = i.encounter[0].reference.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                        EncounterSibling encounter = processor.encounters[encounterId].Get(refSiblingNumber);
                        encounterRef = ResourceGroupProcessor.encounterPrefix + encounter.id;
                        patientRef = encounter.subjectRef;
                        practitionerRef = encounter.participantRef;
                        break;
                    }
                }
            }
            if (encounterRef == null)
            {
                string patientId = originalJson.patient.reference.Substring(ResourceGroupProcessor.patientPrefix.Length);
                PatientSibling patient = processor.patients[patientId].Get(refSiblingNumber);
                patientRef = ResourceGroupProcessor.patientPrefix + patient.id;
                string practitionerId = originalJson.provider.reference.Substring(ResourceGroupProcessor.practitionerPrefix.Length);
                PractitionerSibling practitioner = processor.practitioners[practitionerId].Get(refSiblingNumber);
                practitionerRef = ResourceGroupProcessor.practitionerPrefix + practitioner.id;
            }
            // We have all the data, update references on the clone.
            cloneJson.id = Guid.NewGuid().ToString();
            if (cloneJson.contained != null)
            {
                foreach(ExplanationOfBenefit.Contained contained in cloneJson.contained)
                {
                    if (contained.resourceType == "ServiceRequest")
                    {
                        contained.subject.reference = patientRef;
                        if (contained.requester != null)
                        {
                            contained.requester.reference = practitionerRef;// TODO: if more than one "ServiceRequest", all requesters set to same practitioner?
                        }
                        if (contained.performer != null)
                        {
                            if (contained.performer.Length != 1)
                            {
                                // Resize to 1. TODO, should we assign other performers as well, instead of resizing? Then we need to validate and select them first as well.
                                ExplanationOfBenefit.Performer tmp = contained.performer[0];
                                contained.performer = new ExplanationOfBenefit.Performer[1];
                                contained.performer[0] = tmp;
                            }
                            contained.performer[0].reference = practitionerRef;
                        }
                    }
                    else if (contained.resourceType == "Coverage")
                    {
                        contained.beneficiary.reference = patientRef;
                    }
                }
            }
            cloneJson.patient.reference = patientRef;
            cloneJson.provider.reference = practitionerRef;
            if (claimRef == null)
            {
                cloneJson.claim = null;
            }
            else
            {
                if (cloneJson.claim == null)
                {
                    cloneJson.claim = new ExplanationOfBenefit.Claim();
                }
                cloneJson.claim.reference = claimRef;
            }
            if (cloneJson.careTeam != null)
            {
                foreach (ExplanationOfBenefit.Careteam ct in cloneJson.careTeam)
                {
                    ct.provider.reference = practitionerRef;// TODO: all element of array same practitioner?
                }
            }
            if (cloneJson.item != null)
            {
                // We have items, loop through items and set encounters.
                foreach (ExplanationOfBenefit.Item item in cloneJson.item)
                {
                    if (encounterRef == null)
                    {
                        // We do not have encounter ref, set encounters to null.
                        item.encounter = null;
                    }
                    else
                    {
                        // We do have encounter ref, if there is item encounter array, set to single element with our encounter ref.
                        if (item.encounter != null)
                        {
                            if (item.encounter.Length != 1)
                            {
                                ExplanationOfBenefit.Encounter tmp = item.encounter[0];
                                item.encounter = new ExplanationOfBenefit.Encounter[1];// TODO: resize to 1 or set all encounters?
                                item.encounter[0] = tmp;
                            }
                            item.encounter[0].reference = encounterRef;
                        }
                    }
                }
            }
            return default;
        }*/
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, ExplanationOfBenefit.Rootobject json, out bool select)
        {
            string resName = ResourceGroupProcessor.ExplanationOfBenefitStr;
            select = true;
            if (json.contained != null)
            {
                if (json.contained.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Resource 'contained' is empty!");
                    select = false;
                    return false;
                }

                for (int i = 0; i < json.contained.Length; i++)
                {
                    ExplanationOfBenefit.Contained c = json.contained[i];
                    if (c.resourceType == "ServiceRequest")
                    {
                        if (c.subject == null)
                        {
                            processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "In 'contained[n]' resource type 'ServiceRequest', 'subject' is null!");
                            select = false;
                            return false;
                        }

                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, c.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
                        {
                            select = false;
                            return false;
                        }

                        if (c.requester != null && !processor.ValidateResourceRefAndSelect(json.id, resName, c.requester.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
                        {
                            select = false;
                            return false;
                        }

                        if (c.performer != null)
                        {
                            if (c.performer.Length == 0)
                            {
                                processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "In 'contained[n]' resource type 'ServiceRequest', 'performer' is empty!");
                                select = false;
                                return false;
                            }

                            foreach (ExplanationOfBenefit.Performer p in c.performer)
                            {
                                if (!processor.ValidateResourceRefAndSelect(json.id, resName, p.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
                                {
                                    select = false;
                                    return false;
                                }
                            }
                        }
                    }
                    else if (c.resourceType == "Coverage")
                    {
                        if (c.beneficiary == null)
                        {
                            processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "In 'contained[n]' resource type 'Coverage', 'beneficiary' is null!");
                            select = false;
                            return false;
                        }

                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, c.beneficiary.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
                        {
                            select = false;
                            return false;
                        }
                    }
                    else
                    {
                        processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ExplanationOfBenefitStr, json.id, "Property 'contained[n]' contains resource type other than 'ServiceRequest' or 'Coverage'!");
                        select = false;
                        return false;
                    }
                }
            }

            if (json.patient == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'patient' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, resName, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.provider == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'provider' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, resName, json.provider.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.claim != null && !processor.ValidateResourceRefAndSelect(json.id, resName, json.claim.reference, ResourceGroupProcessor.ClaimStr, processor.Claims, processor.ClaimIdsRemoved, ref select))
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

                foreach (ExplanationOfBenefit.Careteam ct in json.careTeam)
                {
                    if (ct.provider == null)
                    {
                        processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'careTeam[n].provider' is null!");
                        select = false;
                        return false;
                    }

                    if (!processor.ValidateResourceRefAndSelect(json.id, resName, ct.provider.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            if (json.item != null)
            {
                if (json.item.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'item' empty!");
                    select = false;
                    return false;
                }

                foreach (ExplanationOfBenefit.Item i in json.item)
                {
                    if (i.encounter != null)
                    {
                        if (i.encounter.Length == 0)
                        {
                            processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'item[n].encounter' is empty!");
                            select = false;
                            return false;
                        }

                        foreach (ExplanationOfBenefit.Encounter e in i.encounter)
                        {
                            if (!processor.ValidateResourceRefAndSelect(json.id, resName, e.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref select))
                            {
                                select = false;
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        internal struct ExplanationOfBenefitSibling
        {
        }
    }
}
