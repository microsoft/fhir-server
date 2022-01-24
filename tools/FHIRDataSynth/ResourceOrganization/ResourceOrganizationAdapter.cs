using System;

namespace ResourceProcessorNamespace
{
    struct OrganizationSibling
    {
        public string id;
    }

    class OrganizationAdapter : ResourceAdapter<Organization.Rootobject, OrganizationSibling>
    {
        public override OrganizationSibling CreateOriginal(ResourceGroupProcessor processor, Organization.Rootobject json)
        {
            OrganizationSibling r = new OrganizationSibling();
            r.id = json.id;
            return r;
        }

        public override string GetId(Organization.Rootobject json) { return json.id; }

        public override void SetId(Organization.Rootobject json, string id, ResourceGroupProcessor processor) { json.id = id; }

        public override string GetResourceType(Organization.Rootobject json) { return json.resourceType; }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Organization.Rootobject originalJson, Organization.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit) { }

        public override OrganizationSibling CreateClone(ResourceGroupProcessor processor, Organization.Rootobject originalJson, Organization.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            OrganizationSibling r = new OrganizationSibling();
            r.id = cloneJson.id;
            return r;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Organization.Rootobject json, out bool select)
        {
            select = true;
            return true;
        }
    }
}
