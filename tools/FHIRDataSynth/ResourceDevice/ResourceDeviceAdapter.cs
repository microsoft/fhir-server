﻿using System;

namespace ResourceProcessorNamespace
{
    internal struct DeviceSibling
    {
    }

    internal class DeviceAdapter : ResourceAdapter<Device.Rootobject, DeviceSibling>
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
                return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DeviceStr, json.patient.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref select);
            }

            return true;
        }
    }
}
