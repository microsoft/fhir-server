// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal class ResourceClaimAdapter : ResourceAdapterBase<Claim.Rootobject, ResourceClaimAdapter.ClaimSibling>
    {
        public override ClaimSibling CreateOriginal(ResourceGroupProcessor processor, Claim.Rootobject json)
        {
            ClaimSibling r = default(ClaimSibling);
            r.Id = json.id;
            return r;
        }

        public override string GetId(Claim.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Claim.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Claim.Rootobject originalJson, Claim.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
            cloneJson.provider.reference = CloneOrLimit(clone, originalJson, originalJson.provider.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.prescription != null)
            {
                cloneJson.prescription.reference = CloneOrLimit(clone, originalJson, originalJson.prescription.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.item != null)
            {
                for (int i = 0; i < cloneJson.item.Length; i++)
                {
                    Claim.Encounter[] e = cloneJson.item[i].encounter;
                    if (e != null)
                    {
                        for (int j = 0; j < e.Length; j++)
                        {
                            e[j].reference = CloneOrLimit(clone, originalJson, originalJson.item[i].encounter[j].reference, refSiblingNumber, ref refSiblingNumberLimit);
                        }
                    }
                }
            }
        }

        public override ClaimSibling CreateClone(ResourceGroupProcessor processor, Claim.Rootobject originalJson, Claim.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            ClaimSibling r = default(ClaimSibling);
            r.Id = cloneJson.id;
            return r;
        }

        /*public override ClaimSibling CreateClone(ResourceGroupProcessor processor, Claim.Rootobject originalJson, Claim.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            MedicationRequestSibling medicationRequest;
            // First try to get MedicationRequest.
            if (originalJson.prescription != null)
            {
                string medicationRequestId = originalJson.prescription.reference.Substring(ResourceGroupProcessor.medicationRequestPrefix.Length);// Already validated reference is there and is of type 'MedicationRequest' if prescription is non-null.
                medicationRequest = processor.medicationRequests[medicationRequestId].Get(refSiblingNumber);
            }
            else
            {
                // There is no MedicationRequest, create clone from Encounter.
                medicationRequest.id = null;
                // Already validated we have at least one encounter in the arrays.
                string encounterRef = null;
                for (int i = 0; i < originalJson.item.Length; i++)
                {
                    Claim.Item item = originalJson.item[i];
                    if (item.encounter != null)
                    {
                        encounterRef = item.encounter[0].reference;
                    }
                    if (encounterRef != null) break;
                }
                string encounterId = encounterRef.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                medicationRequest.encounter = processor.encounters[encounterId].Get(refSiblingNumber);
            }
            cloneJson.patient.reference = medicationRequest.encounter.subjectRef;
            cloneJson.provider.reference = medicationRequest.encounter.serviceProviderRef;
            cloneJson.prescription = null;
            if (medicationRequest.id != null)
            {
                cloneJson.prescription = new Claim.Prescription();
                cloneJson.prescription.reference = ResourceGroupProcessor.medicationRequestPrefix + medicationRequest.id;
            }
            // IMPORTANT, we use clone item so original is not modified!
            // Already validated we have at least one encounter in the arrays.
            Claim.Item[] newItem = new Claim.Item[1];
            for (int i = 0; i < cloneJson.item.Length; i++)
            {
                Claim.Item item = cloneJson.item[i];
                if (item.encounter != null)
                {
                    newItem[0] = item;
                    break;
                }
            }
            newItem[0].encounter = new Claim.Encounter[1];
            newItem[0].encounter[0] = new Claim.Encounter();
            newItem[0].encounter[0].reference = ResourceGroupProcessor.encounterPrefix + medicationRequest.encounter.id;
            cloneJson.item = newItem;

            ClaimSibling r = new ClaimSibling();
            r.id = cloneJson.id;
            r.encounter = medicationRequest.encounter;
            return r;
        }*/
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Claim.Rootobject json, out bool select)
        {
            select = true;
            if (json.patient == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ClaimStr, json.id, "Property 'patient' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ClaimStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.provider == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ClaimStr, json.id, "Property 'provider' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ClaimStr, json.provider.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.prescription != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ClaimStr, json.prescription.reference, ResourceGroupProcessor.MedicationRequestStr, processor.MedicationRequests, processor.MedicationRequestIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.item != null)
            {
                if (json.item.Length < 1)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ClaimStr, json.id, "Property 'item' contains no elements!");
                    select = false;
                    return false;
                }

                for (int i = 0; i < json.item.Length; i++)
                {
                    if (json.item[i].encounter != null)
                    {
                        if (json.item[i].encounter.Length < 1)
                        {
                            processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ClaimStr, json.id, "Property 'item[i].encounter.Lemgth' contains no elements!");
                            select = false;
                            return false;
                        }

                        for (int j = 0; j < json.item[i].encounter.Length; j++)
                        {
                            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ClaimStr, json.item[i].encounter[j].reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref select))
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

        internal struct ClaimSibling
        {
            public string Id;
        }
    }
}
