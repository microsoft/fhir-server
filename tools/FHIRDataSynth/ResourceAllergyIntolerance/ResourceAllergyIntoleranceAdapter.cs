// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourceAllergyIntoleranceAdapter : ResourceAdapterBase<AllergyIntolerance.Rootobject, ResourceAllergyIntoleranceAdapter.AllergyIntoleranceSibling>
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

            return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.AllergyIntoleranceStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select);
        }

        internal struct AllergyIntoleranceSibling
        {
        }
    }
}
