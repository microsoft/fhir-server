// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;

namespace ResourceProcessorNamespace
{
    internal class ResourceXStructureDefinitionAdapter : ResourceAdapterBase<StructureDefinition.Rootobject, ResourceXStructureDefinitionAdapter.StructureDefinitionSibling>
    {
        public override StructureDefinitionSibling CreateOriginal(ResourceGroupProcessor processor, StructureDefinition.Rootobject json)
        {
            return default;
        }

        public override string GetId(StructureDefinition.Rootobject json)
        {
            return json.id;
        }

        public override string GetResourceType(StructureDefinition.Rootobject json)
        {
            return json.resourceType;
        }

        protected override void IterateReferences(bool clone, ResourceGroupProcessor processor, StructureDefinition.Rootobject originalJson, StructureDefinition.Rootobject cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
        }

        public override StructureDefinitionSibling CreateClone(ResourceGroupProcessor processor, StructureDefinition.Rootobject originalJson, StructureDefinition.Rootobject cloneJson, int refSiblingNumber)
        {
            cloneJson.id = Guid.NewGuid().ToString();
            int unused = int.MinValue;
            IterateReferences(true, processor, originalJson, cloneJson, refSiblingNumber, ref unused);
            return default;
        }

        public override bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, StructureDefinition.Rootobject json, out bool select)
        {
            select = true;
            return true;
        }

        // Enumerator.
        public override Enumerator GetEnumerator()
        {
            return new Enumerator(Processor, Options);
        }

        internal struct StructureDefinitionSibling
        {
        }

        public class Enumerator : EnumeratorBase<int>
        {
            private int enumerator = -1;

            public Enumerator(ResourceGroupProcessor processor, JsonSerializerOptions options)
                : base(processor, options)
            {
            }

            protected override int InitializerCurrent { get => enumerator; }

            protected override bool InitializerMoveNext()
            {
                enumerator++;
                return enumerator <= 1000;
            }

            protected override StructureDefinition.Rootobject LoadFHIRExampleFile()
            {
                return LoadFHIRExampleFileS();
            }

            protected override void InitializeFHIRExample(StructureDefinition.Rootobject json, int initializer)
            {
                InitializeFHIRExampleS(json);
            }

            private static StructureDefinition.Rootobject LoadFHIRExampleFileS()
            {
                string text = File.ReadAllText("ResourceXStructureDefinition/ResourceXStructureDefinitionExample.json");
                return JsonSerializer.Deserialize<StructureDefinition.Rootobject>(text);
            }

            private static void InitializeFHIRExampleS(StructureDefinition.Rootobject json)
            {
                json.id = Guid.NewGuid().ToString();
            }

            public static int GetResourceSize()
            {
                StructureDefinition.Rootobject json = LoadFHIRExampleFileS();
                InitializeFHIRExampleS(json);
                return JsonSerializer.Serialize(json).Length;
            }

            public override void Reset()
            {
                enumerator = -1;
            }

            public override void Dispose()
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}
