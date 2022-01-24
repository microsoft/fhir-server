using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ResourceProcessorNamespace
{
    internal struct DocumentReferenceSibling
    {
    }

    internal class DocumentReferenceAdapter : ResourceAdapter<DocumentReference.Rootobject, DocumentReferenceSibling>
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
                    if (cloneJson.author[i].reference.StartsWith("#"))
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
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref s))
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
                    if (a.reference.StartsWith("#"))
                    {
                        continue;
                    }

                    if (!processor.ValidateResourceRefAndSelect(json.id, resName, a.reference, ResourceGroupProcessor.PractitionerStr, processor.practitioners, processor.practitionerIdsRemoved, ref s))
                    {
                        select = false;
                        return false;
                    }
                }
            }

            if (
                json.authenticator != null &&
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.authenticator.reference, ResourceGroupProcessor.OrganizationStr, processor.organizations, processor.organizationIdsRemoved, ref s))
            {
                select = false;
                return false;
            }

            if (
                json.custodian != null &&
                !processor.ValidateResourceRefAndSelect(json.id, resName, json.custodian.reference, ResourceGroupProcessor.OrganizationStr, processor.organizations, processor.organizationIdsRemoved, ref s))
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
                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, e.reference, ResourceGroupProcessor.EncounterStr, processor.encounters, processor.encounterIdsRemoved, ref s))
                        {
                            select = false;
                            return false;
                        }
                    }
                }

                if (
                    json.context.sourcePatientInfo != null &&
                    !processor.ValidateResourceRefAndSelect(json.id, resName, json.context.sourcePatientInfo.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref s))
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
                        if (!processor.ValidateResourceRefAndSelect(json.id, resName, r.reference, ResourceGroupProcessor.PatientStr, processor.patients, processor.patientIdsRemoved, ref s))
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
            return new Enumerator(processor, options);
        }

        public class Enumerator : EnumeratorBase<EncounterSibling>
        {
            private Dictionary<string, ResourceSiblingsContainer<EncounterSibling>>.Enumerator enumerator;

            protected override bool InitializerMoveNext()
            {
                return enumerator.MoveNext();
            }

            protected override EncounterSibling InitializerCurrent { get => enumerator.Current.Value.GetOriginal(); }

            public Enumerator(ResourceGroupProcessor processor, JsonSerializerOptions options)
                : base(processor, options)
            {
                enumerator = processor.encounters.GetEnumerator();
            }

            protected override DocumentReference.Rootobject LoadFHIRExampleFile()
            {
                return LoadFHIRExampleFileS();
            }

            protected override void InitializeFHIRExample(DocumentReference.Rootobject json, EncounterSibling initializer)
            {
                InitializeFHIRExampleS(json, initializer);
            }

            private static DocumentReference.Rootobject LoadFHIRExampleFileS()
            {
                string text = File.ReadAllText("ResourceXDocumentReference/ResourceXDocumentReferenceExample.json");
                return JsonSerializer.Deserialize<DocumentReference.Rootobject>(text);
            }

            private static void InitializeFHIRExampleS(DocumentReference.Rootobject json, EncounterSibling initializer)
            {
                json.id = Guid.NewGuid().ToString();
                if (initializer.subjectRef != null)
                {
                    json.subject.reference = initializer.subjectRef;
                    json.context.sourcePatientInfo.reference = initializer.subjectRef;
                    json.context.related[0].reference = initializer.subjectRef;
                }
                else
                {
                    json.subject = null;
                    json.context.sourcePatientInfo = null;
                    json.context.related = null;
                }

                if (initializer.participantRef != null)
                {
                    json.author[0].reference = initializer.participantRef;
                }
                else
                {
                    DocumentReference.Author a = json.author[1];
                    json.author = new DocumentReference.Author[1];
                    json.author[0] = a;
                }

                if (initializer.serviceProviderRef != null)
                {
                    json.authenticator.reference = initializer.serviceProviderRef;
                    json.custodian.reference = initializer.serviceProviderRef;
                }
                else
                {
                    json.authenticator = null;
                    json.custodian = null;
                }

                json.context.encounter[0].reference = ResourceGroupProcessor.EncounterPrefix + initializer.id;
            }

            public static int GetResourceSize()
            {
                DocumentReference.Rootobject json = LoadFHIRExampleFileS();
                EncounterSibling initializer = new EncounterSibling();
                initializer.id = Guid.NewGuid().ToString();
                initializer.subjectRef = ResourceGroupProcessor.PatientPrefix + Guid.NewGuid().ToString();
                initializer.participantRef = ResourceGroupProcessor.PractitionerPrefix + Guid.NewGuid().ToString();
                initializer.serviceProviderRef = ResourceGroupProcessor.OrganizationPrefix + Guid.NewGuid().ToString();
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
            }
        }
    }
}
