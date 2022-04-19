// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal class ResourceEncounterAdapter : ResourceAdapterBase<Encounter.Rootobject, ResourceEncounterAdapter.EncounterSibling>
    {
        public override EncounterSibling CreateOriginal(ResourceGroupProcessor processor, Encounter.Rootobject json)
        {
            EncounterSibling r = default(EncounterSibling);
            r.Id = json.id;
            r.SubjectRef = json.subject?.reference;

            if (json.participant != null && json.participant.Length > 0)
            {
                r.ParticipantRef = json.participant[0].individual?.reference;
            }

            r.ServiceProviderRef = json.serviceProvider?.reference;

            return r;
        }

        public override string GetId(Encounter.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Encounter.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.serviceProvider != null)
            {
                cloneJson.serviceProvider.reference = CloneOrLimit(clone, originalJson, originalJson.serviceProvider.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.participant != null)
            {
                for (int i = 0; i < cloneJson.participant.Length; i++)
                {
                    if (cloneJson.participant[i].individual != null)
                    {
                        cloneJson.participant[i].individual.reference = CloneOrLimit(clone, originalJson, originalJson.participant[i].individual.reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }
            }
        }

        public override EncounterSibling CreateClone(ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            return CreateOriginal(processor, cloneJson);
        }

        /*public override EncounterSibling CreateClone(ResourceGroupProcessor processor, Encounter.Rootobject originalJson, Encounter.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            cloneJson.subject.reference = ResourceGroupProcessor.patientPrefix + processor.patients[originalJson.subject.reference.Substring(ResourceGroupProcessor.patientPrefix.Length)].Get(refSiblingNumber).id;
            cloneJson.serviceProvider.reference = ResourceGroupProcessor.organizationPrefix + processor.organizations[originalJson.serviceProvider.reference.Substring(ResourceGroupProcessor.organizationPrefix.Length)].Get(refSiblingNumber).id;
            // NOTE: size stays same in originalJson and cloneJson!
            for (int i = 0; i < originalJson.participant.Length; i++)
            {
                cloneJson.participant[i].individual.reference = ResourceGroupProcessor.practitionerPrefix + processor.practitioners[originalJson.participant[i].individual.reference.Substring(ResourceGroupProcessor.practitionerPrefix.Length)].Get(refSiblingNumber).id;
            }
            return CreateOriginal(processor, cloneJson);
        }*/
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Encounter.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.EncounterStr, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.serviceProvider != null &&
                !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.EncounterStr, json.serviceProvider.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.participant != null)
            {
                if (json.participant.Length < 1)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), ResourceGroupProcessor.EncounterStr, json.id, "Property 'participants' contains 0 elements!");
                    select = false;
                    return false;
                }

                for (int i = 0; i < json.participant.Length; i++)
                {
                    if (json.participant[i].individual != null &&
                        !processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.EncounterStr, json.participant[i].individual.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            return true;
        }

        internal struct EncounterSibling
        {
            public string Id;
            public string SubjectRef; // Patient.
            public string ParticipantRef; // Practitioner.
            public string ServiceProviderRef; // Organization.
        }
    }
}
