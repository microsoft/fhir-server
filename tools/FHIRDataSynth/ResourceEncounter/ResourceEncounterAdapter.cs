using System;

namespace ResourceProcessorNamespace
{
    struct EncounterSibling
    {
        public string id;
        public string subjectRef;// Patient.
        //public string subjectDisplay;// Name.
        public string participantRef;// Practitioner.
        //public string participantDisplay;// Name.
        public string serviceProviderRef;// Organization.
        //public string serviceProviderDisplay;// Name.
    }
    class EncounterAdapter : ResourceAdapter<Encounter.Rootobject, EncounterSibling>
    {
        public override EncounterSibling CreateOriginal(ResourceGroupProcessor processor, Encounter.Rootobject json)
        {
            EncounterSibling r = new EncounterSibling();
            r.id = json.id;
            r.subjectRef = json.subject.reference;
            r.participantRef = json.participant[0].individual.reference;// Already validated that array contains at least one element.
            r.serviceProviderRef = json.serviceProvider.reference;
            return r;
        }
        public override string GetId(Encounter.Rootobject json) { return json.id; }
        public override string GetResourceType(Encounter.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
            if (cloneJson.serviceProvider != null)
            {
                cloneJson.serviceProvider.reference = CloneOrLimit(clone, originalJson, originalJson.serviceProvider.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
            if (cloneJson.participant != null)
            {
                for (int i = 0; i < cloneJson.participant.Length; i++)
                {
                    if (cloneJson.participant[i].individual != null)
                    {
                        cloneJson.participant[i].individual.reference = CloneOrLimit(clone, originalJson, originalJson.participant[i].individual.reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }
            }
        }
        public override EncounterSibling CreateClone(ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            return CreateOriginal(processor, cloneJson);
        }
        /*public override EncounterSibling CreateClone(ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            cloneJson.subject.reference = ResourceGroupProcessor.patientPrefix + processor.patients[originalJson.subject.reference.Substring(ResourceGroupProcessor.patientPrefix.Length)].Get(refSiblingNumber).id;
            cloneJson.serviceProvider.reference = ResourceGroupProcessor.organizationPrefix + processor.organizations[originalJson.serviceProvider.reference.Substring(ResourceGroupProcessor.organizationPrefix.Length)].Get(refSiblingNumber).id;
            // NOTE: size stays same in originalJson and cloneJson!
            for (int i = 0; i < originalJson.participant.Length; i++)
            {
                cloneJson.participant[i].individual.reference = ResourceGroupProcessor.practitionerPrefix + processor.practitioners[originalJson.participant[i].individual.reference.Substring(ResourceGroupProcessor.practitionerPrefix.Length)].Get(refSiblingNumber).id;
            }
            return CreateOriginal(processor, cloneJson);
        }*/
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Encounter.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.encounterStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            if (json.serviceProvider != null &&
                ! processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.encounterStr, json.serviceProvider.reference, ResourceGroupProcessor.organizationStr, processor.organizations, processor.organizationIdsRemoved, ref select)
                )
            {
                select = false;
                return false;
            }
            if (json.participant != null)
            {
                if (json.participant.Length < 1)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.encounterStr, json.id, "Property 'participants' contains 0 elements!");
                    select = false;
                    return false;
                }
                for (int i = 0; i < json.participant.Length; i++)
                {
                    if (json.participant[i].individual != null &&
                        !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.encounterStr, json.participant[i].individual.reference, ResourceGroupProcessor.practitionerStr, processor.practitioners, processor.practitionerIdsRemoved, ref select))
                    {
                        select = false;
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
