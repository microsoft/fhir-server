// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ResourceProcessorNamespace
{
    internal sealed class ResourceXDocumentReferenceAdapter : ResourceAdapterBase<DocumentReference.Rootobject, ResourceXDocumentReferenceAdapter.DocumentReferenceSibling>
    {
        public override DocumentReferenceSibling CreateOriginal(ResourceGroupProcessor processor, DocumentReference.Rootobject json)
        {
            return default;
        }

        public override string GetId(DocumentReference.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(DocumentReference.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, DocumentReference.Rootobject originalJson, DocumentReference.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.author != null)
            {
                for (int i = 0; i < cloneJson.author.Length; i++)
                {
                    if (cloneJson.author[i].reference.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    cloneJson.author[i].reference = CloneOrLimit(clone, originalJson, originalJson.author[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                }
            }

            if (cloneJson.authenticator != null)
            {
                cloneJson.authenticator.reference = CloneOrLimit(clone, originalJson, originalJson.authenticator.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.custodian != null)
            {
                cloneJson.custodian.reference = CloneOrLimit(clone, originalJson, originalJson.custodian.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }

            if (cloneJson.context != null)
            {
                if (cloneJson.context.encounter != null)
                {
                    for (int i = 0; i < cloneJson.context.encounter.Length; i++)
                    {
                        cloneJson.context.encounter[i].reference = CloneOrLimit(clone, originalJson, originalJson.context.encounter[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }

                if (cloneJson.context.sourcePatientInfo != null)
                {
                    cloneJson.context.sourcePatientInfo.reference = CloneOrLimit(clone, originalJson, originalJson.context.sourcePatientInfo.reference, refSiblingNumber, ref refSiblingNumberLimit);
                }

                if (cloneJson.context.related != null)
                {
                    for (int i = 0; i < cloneJson.context.related.Length; i++)
                    {
                        cloneJson.context.related[i].reference = CloneOrLimit(clone, originalJson, originalJson.context.related[i].reference, refSiblingNumber, ref refSiblingNumberLimit);
                    }
                }
            }
        }

        public override DocumentReferenceSibling CreateClone(ResourceGroupProcessor processor, DocumentReference.Rootobject originalJson, DocumentReference.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, DocumentReference.Rootobject json, out bool select)
        {
            string resName = ResourceGroupProcessor.DocumentReferenceStr;
            bool s = true;

            if (
                json.subject != null &&
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (json.author != null)
            {
                if (json.author.Length == 0)
                {
                    processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'author' is empty!");
                    select = false;
                    return false;
                }

                foreach (DocumentReference.Author a in json.author)
                {
                    if (a.reference.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!processor.ValidateResourceRefAndSelect(json.id, resName, a.reference, ResourceGroupProcessor.PractitionerStr, processor.Practitioners, processor.PractitionerIdsRemoved, ref s))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            if (
                json.authenticator != null &&
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.authenticator.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (
                json.custodian != null &&
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.custodian.reference, ResourceGroupProcessor.OrganizationStr, processor.Organizations, processor.OrganizationIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (json.context != null)
            {
                if (json.context.encounter != null)
                {
                    if (json.context.encounter.Length == 0)
                    {
                        processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'context.encounter' is empty!");
                        select = false;
                        return false;
                    }

                    foreach (DocumentReference.Encounter e in json.context.encounter)
                    {
                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, e.reference, ResourceGroupProcessor.EncounterStr, processor.Encounters, processor.EncounterIdsRemoved, ref s))
                        {
                            select = false;
                            return false;
                        }
                    }
                }

                if (
                    json.context.sourcePatientInfo != null &&
                    !processor.ValidateResourceRefAndSelect(json.id, resName, json.context.sourcePatientInfo.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref s))
                {
                    select = false;
                    return false;
                }

                if (json.context.related != null)
                {
                    if (json.context.related.Length == 0)
                    {
                        processor.LogWarning(processor.GetResourceGroupDir(), resName, json.id, "Property 'context.related' is empty!");
                        select = false;
                        return false;
                    }

                    foreach (DocumentReference.Related r in json.context.related)
                    {
                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, r.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref s))
                        {
                            select = false;
                            return false;
                        }
                    }
                }
            }

            select = s;
            return true;
        }

        // Enumerator.
        public override Enumerator GetEnumerator()
        {
            return new Enumerator(Processor, Options);
        }

        internal struct DocumentReferenceSibling
        {
        }

        public sealed class Enumerator : EnumeratorBase<ResourceEncounterAdapter.EncounterSibling>
        {
            private Dictionary<string, ResourceSiblingsContainer<ResourceEncounterAdapter.EncounterSibling>>.Enumerator enumerator;

            public Enumerator(ResourceGroupProcessor processor, JsonSerializerOptions options)
                : base(processor, options)
            {
                enumerator = processor.Encounters.GetEnumerator();
            }

            protected override ResourceEncounterAdapter.EncounterSibling InitializerCurrent { get => enumerator.Current.Value.GetOriginal(); }

            protected override bool InitializerMoveNext()
            {
                return enumerator.MoveNext();
            }

            protected override DocumentReference.Rootobject LoadFHIRExampleFile()
            {
                return LoadFHIRExampleFileS();
            }

            protected override void InitializeFHIRExample(DocumentReference.Rootobject json, ResourceEncounterAdapter.EncounterSibling initializer)
            {
                InitializeFHIRExampleS(json, initializer);
            }

            private static DocumentReference.Rootobject LoadFHIRExampleFileS()
            {
                string text = File.ReadAllText("ResourceXDocumentReference/ResourceXDocumentReferenceExample.json");
                return JsonSerializer.Deserialize<DocumentReference.Rootobject>(text);
            }

            private static void InitializeFHIRExampleS(DocumentReference.Rootobject json, ResourceEncounterAdapter.EncounterSibling initializer)
            {
                json.id = Guid.NewGuid().ToString();
                if (initializer.SubjectRef != null)
                {
                    json.subject.reference = initializer.SubjectRef;
                    json.context.sourcePatientInfo.reference = initializer.SubjectRef;
                    json.context.related[0].reference = initializer.SubjectRef;
                }
                else
                {
                    json.subject = null;
                    json.context.sourcePatientInfo = null;
                    json.context.related = null;
                }

                if (initializer.ParticipantRef != null)
                {
                    json.author[0].reference = initializer.ParticipantRef;
                }
                else
                {
                    DocumentReference.Author a = json.author[1];
                    json.author = new DocumentReference.Author[1];
                    json.author[0] = a;
                }

                if (initializer.ServiceProviderRef != null)
                {
                    json.authenticator.reference = initializer.ServiceProviderRef;
                    json.custodian.reference = initializer.ServiceProviderRef;
                }
                else
                {
                    json.authenticator = null;
                    json.custodian = null;
                }

                json.context.encounter[0].reference = ResourceGroupProcessor.EncounterPrefix + initializer.Id;
            }

            public static int GetResourceSize()
            {
                DocumentReference.Rootobject json = LoadFHIRExampleFileS();
                ResourceEncounterAdapter.EncounterSibling initializer = default(ResourceEncounterAdapter.EncounterSibling);
                initializer.Id = Guid.NewGuid().ToString();
                initializer.SubjectRef = ResourceGroupProcessor.PatientPrefix + Guid.NewGuid().ToString();
                initializer.ParticipantRef = ResourceGroupProcessor.PractitionerPrefix + Guid.NewGuid().ToString();
                initializer.ServiceProviderRef = ResourceGroupProcessor.OrganizationPrefix + Guid.NewGuid().ToString();
                InitializeFHIRExampleS(json, initializer);
                return JsonSerializer.Serialize(json).Length;
            }

            public override void Reset()
            {
                ((IEnumerator)enumerator).Reset();
            }

            public override void Dispose()
            {
                enumerator.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
