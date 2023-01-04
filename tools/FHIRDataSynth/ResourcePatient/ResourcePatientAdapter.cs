// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourcePatientAdapter : ResourceAdapterBase<Patient.Rootobject, ResourcePatientAdapter.PatientSibling>
    {
        public override PatientSibling CreateOriginal(ResourceGroupProcessor processor, Patient.Rootobject json)
        {
            PatientSibling r = default(PatientSibling);
            r.Id = json.id;
            return r;
        }

        public override string GetId(Patient.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Patient.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Patient.Rootobject originalJson, Patient.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
        }

        public override PatientSibling CreateClone(ResourceGroupProcessor processor, Patient.Rootobject originalJson, Patient.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            PatientSibling r = default(PatientSibling);
            r.Id = cloneJson.id;
            return r;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Patient.Rootobject json, out bool select)
        {
            select = true;
            return true;
        }

        internal struct PatientSibling
        {
            public string Id;
        }
    }
}
