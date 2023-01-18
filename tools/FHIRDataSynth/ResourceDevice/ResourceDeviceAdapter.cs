// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourceDeviceAdapter : ResourceAdapterBase<Device.Rootobject, ResourceDeviceAdapter.DeviceSibling>
    {
        public override DeviceSibling CreateOriginal(ResourceGroupProcessor processor, Device.Rootobject json)
        {
            return default;
        }

        public override string GetId(Device.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Device.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Device.Rootobject originalJson, Device.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.patient != null)
            {
                cloneJson.patient.reference = CloneOrLimit(clone, originalJson, originalJson.patient.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }

        public override DeviceSibling CreateClone(ResourceGroupProcessor processor, Device.Rootobject originalJson, Device.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Device.Rootobject json, out bool select)
        {
            select = true;
            if (json.patient != null)
            {
                return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DeviceStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select);
            }

            return true;
        }

        internal struct DeviceSibling
        {
        }
    }
}
