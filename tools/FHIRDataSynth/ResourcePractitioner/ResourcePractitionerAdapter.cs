using System;

namespace ResourceProcessorNamespace
{
    struct PractitionerSibling
    {
        public string id;
        //public string name;
    }
    class PractitionerAdapter : ResourceAdapter<Practitioner.Rootobject, PractitionerSibling>
    {
        public override PractitionerSibling CreateOriginal(ResourceGroupProcessor processor, Practitioner.Rootobject json)
        {
            PractitionerSibling r = new PractitionerSibling();
            r.id = json.id;
            return r;
        }
        public override string GetId(Practitioner.Rootobject json) { return json.id; }
        public override string GetResourceType(Practitioner.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Practitioner.Rootobject originalJson, Practitioner.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit) { }
        public override PractitionerSibling CreateClone(ResourceGroupProcessor processor, Practitioner.Rootobject originalJson, Practitioner.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            PractitionerSibling r = new PractitionerSibling();
            r.id = cloneJson.id;
            return r;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Practitioner.Rootobject json, out bool select) { select = true; return true; }
    }
}
