// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FHIRDataSynth;

namespace ResourceProcessorNamespace
{
    internal struct ResourceSiblingsContainer<TS>
        where TS : struct
    {
        private TS[] siblings;

        public ResourceSiblingsContainer(TS[] siblings)
        {
            this.siblings = siblings;
        }

        public int Count { get => siblings.Length; }

        public ref TS Get(int siblingNumber, string resourceGroupDir, string resourceName, string resourceId)
        {
            if (siblingNumber >= Count)
            {
                throw new FHIRDataSynthException(resourceGroupDir, resourceName, resourceId, "Sibling array index too big.");
            }

            return ref siblings[siblingNumber];
        }

        public ref TS GetOriginal()
        {
            return ref siblings[0]; // There is always at least one and first one is always original sibling.
        }
    }
}
