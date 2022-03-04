// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal class ResourceDiagnosticReportAdapter : ResourceAdapterBase<DiagnosticReport.Rootobject, ResourceDiagnosticReportAdapter.DiagnosticReportSibling>
    {
        public override DiagnosticReportSibling CreateOriginal(ResourceGroupProcessor processor, DiagnosticReport.Rootobject json)
        {
            return default;
        }

        public override string GetId(DiagnosticReport.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(DiagnosticReport.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, DiagnosticReport.Rootobject originalJson, DiagnosticReport.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.result != null)
            {
                for (int i = 0; i < cloneJson.result.Length; i++)
                {
                    cloneJson.result[i].reference = CloneOrLimit(clone, originalJson, originalJson.result[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                }
            }
        }

        public override DiagnosticReportSibling CreateClone(ResourceGroupProcessor processor, DiagnosticReport.Rootobject originalJson, DiagnosticReport.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, DiagnosticReport.Rootobject json, out bool select)
        {
            bool s = true;
            if (json.subject != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DiagnosticReportStr, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (json.encounter != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DiagnosticReportStr, json.encounter.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (json.result != null)
            {
                if (json.result.Length < 1)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.DiagnosticReportStr, json.id, "Property 'result' contains 0 elements!");
                    select = false;
                    return false;
                }

                for (int i = 0; i < json.result.Length; i++)
                {
                    if (!processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DiagnosticReportStr, json.result[i].reference, ResourceGroupProcessor.ObservationStr, processor.Observations, processor.ObservationIdsRemoved, ref s))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            select = s;
            return true;
        }

        internal struct DiagnosticReportSibling
        {
        }
    }
}
