// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace ResourceProcessorNamespace
{
    internal class ResourceCareTeamAdapter : ResourceAdapterBase<CareTeam.Rootobject, ResourceCareTeamAdapter.CareTeamSibling>
    {
        public override CareTeamSibling CreateOriginal(ResourceGroupProcessor processor, CareTeam.Rootobject json)
        {
            CareTeamSibling ret = default(CareTeamSibling);
            ret.Id = json.id;
            /*if (json.subject != null)
            {
                ret.subjectRef = json.subject.reference;
            }
            if (json.encounter != null)
            {
                ret.encounterRef = json.encounter.reference;
            }
            return ret;*/
            return ret;
        }

        public override string GetId(CareTeam.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(CareTeam.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, CareTeam.Rootobject originalJson, CareTeam.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.encounter != null)
            {
                cloneJson.encounter.reference = CloneOrLimit(clone, originalJson, originalJson.encounter.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.participant != null)
            {
                for (int i = 0; i < cloneJson.participant.Length; i++)
                {
                    CareTeam.Member m = cloneJson.participant[i].member;
                    if (m != null)
                    {
                        m.reference = CloneOrLimit(clone, originalJson, originalJson.participant[i].member.reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }
            }

            if (cloneJson.managingOrganization != null)
            {
                for (int i = 0; i < cloneJson.managingOrganization.Length; i++)
                {
                    cloneJson.managingOrganization[i].reference = CloneOrLimit(clone, originalJson, originalJson.managingOrganization[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                }
            }
        }

        public override CareTeamSibling CreateClone(ResourceGroupProcessor processor, CareTeam.Rootobject originalJson, CareTeam.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);

            CareTeamSibling r = default(CareTeamSibling);
            r.Id = cloneJson.id;
            return r;
        }

        /*public override CareTeamSibling CreateClone(
            ResourceGroupProcessor processor,
            // WARNING! originalJson MUST not be modified, member classes of originalJson MUST not be asigned to cloneJson!
            CareTeam.Rootobject originalJson,
            CareTeam.Rootobject cloneJson,
            int refSiblingNumber)
        {
            string patientRef = null;
            string practitionerRef = null;
            string organizationRef = null;
            string encounterRef = null;
            // Get references.
            if (originalJson.encounter?.reference != null)
            {
                string encounterId = originalJson.encounter.reference.Substring(ResourceGroupProcessor.encounterPrefix.Length);
                EncounterSibling encounter = processor.encounters[encounterId].Get(refSiblingNumber);
                encounterRef = ResourceGroupProcessor.encounterPrefix + encounter.id;
                patientRef = encounter.subjectRef;
                practitionerRef = encounter.participantRef;
                organizationRef = encounter.serviceProviderRef;
            }
            if (patientRef == null && originalJson.subject?.reference != null)
            {
                string patientId = originalJson.subject.reference.Substring(ResourceGroupProcessor.patientPrefix.Length);
                PatientSibling patient = processor.patients[patientId].Get(refSiblingNumber);
                patientRef = ResourceGroupProcessor.patientPrefix + patient.id;
            }
            if (organizationRef == null && originalJson.managingOrganization?[0].reference != null)// Already validated, if managingOrganization != null, must not be empty.
            {
                string organizationId = originalJson.managingOrganization[0].reference.Substring(ResourceGroupProcessor.organizationPrefix.Length);
                OrganizationSibling organization = processor.organizations[organizationId].Get(refSiblingNumber);
                organizationRef = ResourceGroupProcessor.organizationPrefix + organization.id;
            }
            if ((patientRef == null || practitionerRef == null || organizationRef == null) && originalJson.participant != null)
            {
                foreach (CareTeam.Participant p in originalJson.participant)
                {
                    if (p.member?.reference != null)
                    {
                        if (patientRef == null && p.member.reference.StartsWith(ResourceGroupProcessor.patientPrefix))
                        {
                            string patientId = p.member.reference.Substring(ResourceGroupProcessor.patientPrefix.Length);
                            PatientSibling patient = processor.patients[patientId].Get(refSiblingNumber);
                            patientRef = ResourceGroupProcessor.patientPrefix + patient.id;
                        }
                        else if (practitionerRef == null && p.member.reference.StartsWith(ResourceGroupProcessor.practitionerPrefix))
                        {
                            string practitionerId = p.member.reference.Substring(ResourceGroupProcessor.practitionerPrefix.Length);
                            PractitionerSibling practitioner = processor.practitioners[practitionerId].Get(refSiblingNumber);
                            practitionerRef = ResourceGroupProcessor.practitionerPrefix + practitioner.id;
                        }
                        else if (organizationRef == null && p.member.reference.StartsWith(ResourceGroupProcessor.organizationPrefix))
                        {
                            string organizationId = p.member.reference.Substring(ResourceGroupProcessor.organizationPrefix.Length);
                            OrganizationSibling organization = processor.organizations[organizationId].Get(refSiblingNumber);
                            organizationRef = ResourceGroupProcessor.organizationPrefix + organization.id;
                        }
                    }
                }
            }
            // Have new references. Set new references in clone.
            cloneJson.id = Guid.NewGuid().ToString();
            if (cloneJson.subject?.reference != null)
            {
                if (patientRef != null)
                {
                    cloneJson.subject.reference = patientRef;
                }
                else
                {
                    cloneJson.subject = null;
                }
            }
            if (cloneJson.encounter?.reference != null)
            {
                if (encounterRef != null)
                {
                    cloneJson.encounter.reference = encounterRef;
                }
                else
                {
                    cloneJson.encounter = null;
                }
            }
            if (cloneJson.participant != null)
            {
                CareTeam.Participant patientParticipant = null;
                CareTeam.Participant practitionerParticipant = null;
                CareTeam.Participant organizationParticipant = null;
                int participantCount = 0;
                foreach (CareTeam.Participant p in cloneJson.participant)
                {
                    if (p.member?.reference != null)
                    {
                        if (patientRef != null && patientParticipant == null && p.member.reference.StartsWith(ResourceGroupProcessor.patientPrefix))
                        {
                            patientParticipant = p;
                            patientParticipant.member.reference = patientRef;
                            participantCount++;
                        }
                        else if (practitionerRef != null && practitionerParticipant == null && p.member.reference.StartsWith(ResourceGroupProcessor.practitionerPrefix))
                        {
                            practitionerParticipant = p;
                            practitionerParticipant.member.reference = practitionerRef;
                            participantCount++;
                        }
                        else if (organizationRef != null && organizationParticipant == null && p.member.reference.StartsWith(ResourceGroupProcessor.organizationPrefix))
                        {
                            organizationParticipant = p;
                            organizationParticipant.member.reference = organizationRef;
                            participantCount++;
                        }
                    }
                }
                if (cloneJson.participant.Length != participantCount)
                {
                    cloneJson.participant = new CareTeam.Participant[participantCount];
                }
                participantCount = 0;
                if (patientParticipant != null)
                {
                    cloneJson.participant[participantCount++] = patientParticipant;
                }
                if (practitionerParticipant != null)
                {
                    cloneJson.participant[participantCount++] = practitionerParticipant;
                }
                if (organizationParticipant != null)
                {
                    cloneJson.participant[participantCount++] = organizationParticipant;
                }
            }
            if (cloneJson.managingOrganization != null)
            {
                if (organizationRef != null)
                {
                    CareTeam.Managingorganization m = cloneJson.managingOrganization[0];// Already validated, if managingOrganization != null, must not be empty.
                    m.reference = organizationRef;
                    if (cloneJson.managingOrganization.Length != 1)
                    {
                        cloneJson.managingOrganization = new CareTeam.Managingorganization[1];
                        cloneJson.managingOrganization[0] = m;
                    }
                }
                else
                {
                    cloneJson.managingOrganization = null;
                }
            }

            // Return clone sibling.
            CareTeamSibling ret = new CareTeamSibling();
            ret.id = cloneJson.id;
            ret.encounterRef = encounterRef;
            ret.subjectRef = patientRef;
            return ret;
        }*/
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, CareTeam.Rootobject json, out bool select)
        {
            string resName = ResourceGroupProcessor.CareTeamStr;
            select = true;

            if (json.subject != null && !processor.ValidateResourceRefAndSelect(json.id, resName, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.encounter != null && !processor.ValidateResourceRefAndSelect(json.id, resName, json.encounter.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref select))
            {
                select = false;
                return false;
            }

            if (json.participant != null)
            {
                if (json.participant.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'participant' is empty!");
                    select = false;
                    return false;
                }

                foreach (CareTeam.Participant p in json.participant)
                {
                    if (p.member != null)
                    {
                        if (p.member.reference != null)
                        {
                            if (p.member.reference.StartsWith(ResourceGroupProcessor.PatientPrefix, StringComparison.Ordinal))
                            {
                                if (!processor.ValidateResourceRefAndSelect(json.id, resName, p.member.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select))
                                {
                                    select = false;
                                    return false;
                                }
                            }
                            else if (p.member.reference.StartsWith(ResourceGroupProcessor.PractitionerPrefix, StringComparison.Ordinal))
                            {
                                if (!processor.ValidateResourceRefAndSelect(json.id, resName, p.member.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref select))
                                {
                                    select = false;
                                    return false;
                                }
                            }
                            else if (p.member.reference.StartsWith(ResourceGroupProcessor.OrganizationPrefix, StringComparison.Ordinal))
                            {
                                if (!processor.ValidateResourceRefAndSelect(json.id, resName, p.member.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref select))
                                {
                                    select = false;
                                    return false;
                                }
                            }
                            else
                            {
                                processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, $"Wrong reference '{p.member.reference}' in property participant[n].member.reference!");
                                select = false;
                                return false;
                            }
                        }
                        else
                        {
                            processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, $"participant[n].member.reference is null!");
                            select = false;
                            return false;
                        }
                    }
                }
            }

            if (json.managingOrganization != null)
            {
                if (json.managingOrganization.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'managingOrganization' is empty!");
                    select = false;
                    return false;
                }

                foreach (CareTeam.Managingorganization m in json.managingOrganization)
                {
                    if (!processor.ValidateResourceRefAndSelect(json.id, resName, m.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref select))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            return true;
        }

        internal struct CareTeamSibling
        {
            public string Id;
            /*public string encounterRef;
            public string subjectRef;*/
        }
    }
}
