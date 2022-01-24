using System;

namespace ResourceProcessorNamespace
{
    internal struct AllergyIntoleranceSibling
    {
    }

    internal class AllergyIntoleranceAdapter : ResourceAdapter<AllergyIntolerance.Rootobject, AllergyIntoleranceSibling>
    {
        public override AllergyIntoleranceSibling CreateOriginal(ResourceGroupProcessor processor, AllergyIntolerance.Rootobject json)
        {
            return default;
        }

        public override string GetId(AllergyIntolerance.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(AllergyIntolerance.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, AllergyIntolerance.Rootobject originalJson, AllergyIntolerance.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
        }

        public override AllergyIntoleranceSibling CreateClone(ResourceGroupProcessor processor, AllergyIntolerance.Rootobject originalJson, AllergyIntolerance.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, AllergyIntolerance.Rootobject json, out bool select)
        {
            select = true;
            if (json.patient == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.AllergyIntoleranceStr, json.id, "Property 'patient' is null!");
                select = false;
                return false;
            }

            return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.AllergyIntoleranceStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref select);
        }
    }
}
