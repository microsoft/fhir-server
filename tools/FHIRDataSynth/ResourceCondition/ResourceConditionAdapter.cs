using System;

namespace ResourceProcessorNamespace
{
    struct ConditionSibling
    {
    }
    class ConditionAdapter : ResourceAdapter<Condition.Rootobject, ConditionSibling>
    {
        public override ConditionSibling CreateOriginal(ResourceGroupProcessor processor, Condition.Rootobject json)
        {
            return default;
        }
        public override string GetId(Condition.Rootobject json) { return json.id; }
        public override string GetResourceType(Condition.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Condition.Rootobject originalJson, Condition.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }
        public override ConditionSibling CreateClone(ResourceGroupProcessor processor, Condition.Rootobject originalJson, Condition.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Condition.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.conditionStr, json.id, "Property 'subject' is null!");
                select = false;
                return false;
            }
            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.conditionStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            if (json.encounter != null)
            {
                return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.conditionStr, json.encounter.reference, ResourceGroupProcessor.encounterStr, processor.encounters, processor.encounterIdsRemoved, ref select);
            }
            return true;
        }
    }
}
