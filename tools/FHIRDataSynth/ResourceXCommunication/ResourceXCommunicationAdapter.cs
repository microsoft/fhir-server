using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ResourceProcessorNamespace
{
    struct CommunicationSibling
    {
    }
    class CommunicationAdapter : ResourceAdapter<Communication.Rootobject, CommunicationSibling>
    {
        public override CommunicationSibling CreateOriginal(ResourceGroupProcessor processor, Communication.Rootobject json)
        {
            return default;
        }
        public override string GetId(Communication.Rootobject json) { return json.id; }
        public override string GetResourceType(Communication.Rootobject json) { return json.resourceType; }
        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, Communication.Rootobject originalJson, Communication.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            if (cloneJson.subject != null)
            {
                cloneJson.subject.reference = CloneOrLimit(clone, originalJson, originalJson.subject.reference, refSiblingNumber, ref refSiblingNumberLimit);
            }
        }
        public override CommunicationSibling CreateClone(ResourceGroupProcessor processor, Communication.Rootobject originalJson, Communication.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }
        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, Communication.Rootobject json, out bool select)
        {
            select = true;
            if (json.subject != null)
            {
                return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.documentReferenceStr, json.subject.reference, ResourceGroupProcessor.patientStr, processor.patients, processor.patientIdsRemoved, ref select);
            }
            select = true;
            return true;
        }
        // Enumerator.
        public override Enumerator GetEnumerator()
        {
            return new Enumerator(processor, options);
        }
        public class Enumerator : EnumeratorBase<PatientSibling>
        {
            Dictionary<string, ResourceSiblingsContainer<PatientSibling>>.Enumerator enumerator;
            protected override bool InitializerMoveNext() { return enumerator.MoveNext(); }
            protected override PatientSibling InitializerCurrent { get => enumerator.Current.Value.GetOriginal(); }

            public Enumerator(ResourceGroupProcessor processor, JsonSerializerOptions options) : base(processor, options) { enumerator = processor.patients.GetEnumerator(); }
            protected override Communication.Rootobject LoadFHIRExampleFile() { return LoadFHIRExampleFileS(); }
            protected override void InitializeFHIRExample(Communication.Rootobject json, PatientSibling initializer) { InitializeFHIRExampleS(json, initializer); }
            private static Communication.Rootobject LoadFHIRExampleFileS()
            {
                string text = File.ReadAllText("ResourceXCommunication/ResourceXCommunicationExample.json");
                return JsonSerializer.Deserialize<Communication.Rootobject>(text);
            }
            private static void InitializeFHIRExampleS(Communication.Rootobject json, PatientSibling initializer)
            {
                json.id = Guid.NewGuid().ToString();
                json.subject.reference = ResourceGroupProcessor.patientPrefix + initializer.id;
            }
            public static int GetResourceSize()
            {
                Communication.Rootobject json = LoadFHIRExampleFileS();
                PatientSibling initializer = new PatientSibling();
                initializer.id = Guid.NewGuid().ToString();
                InitializeFHIRExampleS(json, initializer);
                return JsonSerializer.Serialize(json).Length;
            }
            public override void Reset() { ((IEnumerator)enumerator).Reset(); }
            public override void Dispose() { enumerator.Dispose(); }

        }
    }
}
