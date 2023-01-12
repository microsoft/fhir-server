// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourceImmunizationAdapter : ResourceAdapterBase<Immunization.Rootobject, ResourceImmunizationAdapter.ImmunizationSibling>
    {
        public override ImmunizationSibling CreateOriginal(ResourceGroupProcessor processor, Immunization.Rootobject json)
        {
            return default;
        }

        public override string GetId(Immunization.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Immunization.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Immunization.Rootobject originalJson, Immunization.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }

        public override ImmunizationSibling CreateClone(ResourceGroupProcessor processor, Immunization.Rootobject originalJson, Immunization.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Immunization.Rootobject json, out bool select)
        {
            select = true;
            if (json.patient == null)
            {
                processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.ImmunizationStr, json.id, "Property 'patient' is null!");
                select = false;
                return false;
            }

            if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ImmunizationStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.encounter != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.ImmunizationStr, json.encounter.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            return true;
        }

        internal struct ImmunizationSibling
        {
        }
    }
}
