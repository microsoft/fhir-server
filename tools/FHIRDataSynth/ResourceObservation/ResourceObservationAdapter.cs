using System;

namespace ResourceProcessorNamespace
{
    struct ObservationSibling
    {
        public string id;
    }

    class ObservationAdapter : ResourceAdapter<Observation.Rootobject, ObservationSibling>
    {
        public override ObservationSibling CreateOriginal(ResourceGroupProcessor processor, Observation.Rootobject json)
        {
            ObservationSibling r = new ObservationSibling();
            r.id = json.id;
            /*if (json.encounter != null)
            {
                string encounterId = json.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                r.encounter = processor.encounters[encounterId].GetOriginal();
            }*/
            return r;
        }

        public override string GetId(Observation.Rootobject json) { return json.id; }

        public override string GetResourceType(Observation.Rootobject json) { return json.resourceType; }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Observation.Rootobject originalJson, Observation.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }

        public override ObservationSibling CreateClone(ResourceGroupProcessor processor, Observation.Rootobject originalJson, Observation.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            ObservationSibling r = new ObservationSibling();
            r.id = cloneJson.id;
            /*if (cloneJson.encounter != null)
            {
                r.encounter = processor.encounters[originalJson.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length)].Get_OLD(refSiblingNumber);
            }*/
            return r;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Observation.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.observationStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.encounter != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.observationStr, json.encounter.reference, ResourceGroupProcessor.encounterStr, processor.encounters, processor.encounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            return true;
        }
    }
}
