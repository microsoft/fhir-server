// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourcePractitionerAdapter : ResourceAdapterBase<Practitioner.Rootobject, ResourcePractitionerAdapter.PractitionerSibling>
    {
        public override PractitionerSibling CreateOriginal(ResourceGroupProcessor processor, Practitioner.Rootobject json)
        {
            PractitionerSibling r = default(PractitionerSibling);
            r.Id = json.id;
            return r;
        }

        public override string GetId(Practitioner.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Practitioner.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Practitioner.Rootobject originalJson, Practitioner.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
        }

        public override PractitionerSibling CreateClone(ResourceGroupProcessor processor, Practitioner.Rootobject originalJson, Practitioner.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            PractitionerSibling r = default(PractitionerSibling);
            r.Id = cloneJson.id;
            return r;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Practitioner.Rootobject json, out bool select)
        {
            select = true;
            return true;
        }

        internal struct PractitionerSibling
        {
            public string Id;
        }
    }
}
