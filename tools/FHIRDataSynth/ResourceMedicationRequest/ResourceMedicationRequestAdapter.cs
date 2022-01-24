using System;

namespace ResourceProcessorNamespace
{
    internal struct MedicationRequestSibling
    {
        public string id;
    }

    internal class MedicationRequestAdapter : ResourceAdapter<MedicationRequest.Rootobject, MedicationRequestSibling>
    {
        public override MedicationRequestSibling CreateOriginal(ResourceGroupProcessor processor, MedicationRequest.Rootobject json)
        {
            MedicationRequestSibling r = new MedicationRequestSibling();
            r.id = json.id;
            /*if (json.encounter != null)
            {
                string encounterId = json.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                r.encounter = processor.encounters[encounterId].GetOriginal();
            }*/
            return r;
        }

        public override string GetId(MedicationRequest.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(MedicationRequest.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, MedicationRequest.Rootobject originalJson, MedicationRequest.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.requester != null)
            {
                cloneJson.requester.reference = CloneOrLimit(clone, originalJson, originalJson.requester.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }

        public override MedicationRequestSibling CreateClone(ResourceGroupProcessor processor, MedicationRequest.Rootobject originalJson, MedicationRequest.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            MedicationRequestSibling r = new MedicationRequestSibling();
            r.id = cloneJson.id;
            /*if (cloneJson.encounter != null)
            {
                r.encounter = processor.encounters[originalJson.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length)].Get_OLD(refSiblingNumber);
            }*/
            return r;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, MedicationRequest.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.MedicationRequestStr, json.id, "Property 'subject' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.MedicationRequestStr, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.encounter != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.MedicationRequestStr, json.encounter.reference, ResourceGroupProcessor.EncounterStr, processor.encounters, processor.encounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.requester != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.MedicationRequestStr, json.requester.reference, ResourceGroupProcessor.PractitionerStr, processor.practitioners, processor.practitionerIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            return true;
        }
    }
}
