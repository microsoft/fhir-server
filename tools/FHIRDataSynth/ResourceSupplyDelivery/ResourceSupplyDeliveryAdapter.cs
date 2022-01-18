using System;

namespace ResourceProcessorNamespace
{
    struct SupplyDeliverySibling
    {
    }
    class SupplyDeliveryAdapter : ResourceAdapter<SupplyDelivery.Rootobject, SupplyDeliverySibling>
    {
        public override SupplyDeliverySibling CreateOriginal(ResourceGroupProcessor processor, SupplyDelivery.Rootobject json)
        {
            return default;
        }
        public override string GetId(SupplyDelivery.Rootobject json) { return json.id; }
        public override string GetResourceType(SupplyDelivery.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, SupplyDelivery.Rootobject originalJson, SupplyDelivery.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.patient != null)
            {
                cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }
        public override SupplyDeliverySibling CreateClone(ResourceGroupProcessor processor, SupplyDelivery.Rootobject originalJson, SupplyDelivery.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, SupplyDelivery.Rootobject json, out bool select)
        {
            select = true;
            if (json.patient != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.supplyDeliveryStr, json.patient.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }
            return true;
        }
    }
}
