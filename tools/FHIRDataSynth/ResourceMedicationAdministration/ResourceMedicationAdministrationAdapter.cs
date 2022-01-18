using System;

namespace ResourceProcessorNamespace
{
    struct MedicationAdministrationSibling
    {
    }
    class MedicationAdministrationAdapter : ResourceAdapter<MedicationAdministration.Rootobject, MedicationAdministrationSibling>
    {
        public override MedicationAdministrationSibling CreateOriginal(ResourceGroupProcessor processor, MedicationAdministration.Rootobject json)
        {
            return default;
        }
        public override string GetId(MedicationAdministration.Rootobject json) { return json.id; }
        public override string GetResourceType(MedicationAdministration.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, MedicationAdministration.Rootobject originalJson, MedicationAdministration.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.context != null)
            {
                cloneJson.context.reference = CloneOrLimit(clone, originalJson, originalJson.context.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }
        public override MedicationAdministrationSibling CreateClone(ResourceGroupProcessor processor, MedicationAdministration.Rootobject originalJson, MedicationAdministration.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, MedicationAdministration.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.medicationAdministrationStr, json.id, "Property 'subject' is null!");
                select = false;
                return false;
            }
            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.medicationAdministrationStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            if (json.context != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.medicationAdministrationStr, json.context.reference, ResourceGroupProcessor.encounterStr, processor.encounters, processor.encounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            return true;
        }
    }
}
