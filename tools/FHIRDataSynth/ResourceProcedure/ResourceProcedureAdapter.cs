using System;

namespace ResourceProcessorNamespace
{
    struct ProcedureSibling
    {
    }
    class ProcedureAdapter : ResourceAdapter<Procedure.Rootobject, ProcedureSibling>
    {
        public override ProcedureSibling CreateOriginal(ResourceGroupProcessor processor, Procedure.Rootobject json)
        {
            return default;
        }
        public override string GetId(Procedure.Rootobject json) { return json.id; }
        public override string GetResourceType(Procedure.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Procedure.Rootobject originalJson, Procedure.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }
        public override ProcedureSibling CreateClone(ResourceGroupProcessor processor, Procedure.Rootobject originalJson, Procedure.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Procedure.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.procedureStr, json.id, "Property 'subject' is null!");
                select = false;
                return false;
            }
            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.procedureStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            if (json.encounter != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.procedureStr, json.encounter.reference, ResourceGroupProcessor.encounterStr, processor.encounters, processor.encounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            return true;
        }
    }
}
