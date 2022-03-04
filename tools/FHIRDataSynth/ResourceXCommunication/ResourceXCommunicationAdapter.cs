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
    internal class ResourceXCommunicationAdapter : ResourceAdapterBase<Communication.Rootobject, ResourceXCommunicationAdapter.CommunicationSibling>
    {
        public override CommunicationSibling CreateOriginal(ResourceGroupProcessor processor, Communication.Rootobject json)
        {
            return default;
        }

        public override string GetId(Communication.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(Communication.Rootobject json)
        {
            return json.resourceType;
        }

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
                return processor.ValidateResourceRefAndSelect(json.id, ResourceGroupProcessor.DocumentReferenceStr, json.subject.reference, ResourceGroupProcessor.PatientStr, processor.Patients, processor.PatientIdsRemoved, ref select);
            }

            select = true;
            return true;
        }

        // Enumerator.
        public override Enumerator GetEnumerator()
        {
            return new Enumerator(Processor, Options);
        }

        internal struct CommunicationSibling
        {
        }

        public class Enumerator : EnumeratorBase<ResourcePatientAdapter.PatientSibling>
        {
            private Dictionary<string, ResourceSiblingsContainer<ResourcePatientAdapter.PatientSibling>>.Enumerator enumerator;

            public Enumerator(ResourceGroupProcessor processor, JsonSerializerOptions options)
                : base(processor, options)
            {
                enumerator = processor.Patients.GetEnumerator();
            }

            protected override ResourcePatientAdapter.PatientSibling InitializerCurrent { get => enumerator.Current.Value.GetOriginal(); }

            protected override bool InitializerMoveNext()
            {
                return enumerator.MoveNext();
            }

            protected override Communication.Rootobject LoadFHIRExampleFile()
            {
                return LoadFHIRExampleFileS();
            }

            protected override void InitializeFHIRExample(Communication.Rootobject json, ResourcePatientAdapter.PatientSibling initializer)
            {
                InitializeFHIRExampleS(json, initializer);
            }

            private static Communication.Rootobject LoadFHIRExampleFileS()
            {
                string text = File.ReadAllText("ResourceXCommunication/ResourceXCommunicationExample.json");
                return JsonSerializer.Deserialize<Communication.Rootobject>(text);
            }

            private static void InitializeFHIRExampleS(Communication.Rootobject json, ResourcePatientAdapter.PatientSibling initializer)
            {
                json.id = Guid.NewGuid().ToString();
                json.subject.reference = ResourceGroupProcessor.PatientPrefix + initializer.Id;
            }

            public static int GetResourceSize()
            {
                Communication.Rootobject json = LoadFHIRExampleFileS();
                ResourcePatientAdapter.PatientSibling initializer = default(ResourcePatientAdapter.PatientSibling);
                initializer.Id = Guid.NewGuid().ToString();
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
