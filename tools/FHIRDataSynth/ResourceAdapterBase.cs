// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using FHIRDataSynth;

namespace ResourceProcessorNamespace
{
    internal abstract class ResourceAdapterBase<T, TS>
        where T : class
        where TS : struct
    {
        private ResourceGroupProcessor processor;
        private JsonSerializerOptions options;

        protected ResourceGroupProcessor Processor { get => processor; }

        protected JsonSerializerOptions Options { get => options; }

        public void Initialize(ResourceGroupProcessor processor, JsonSerializerOptions options)
        {
            this.processor = processor;
            this.options = options;
        }

        public abstract TS CreateOriginal(ResourceGroupProcessor processor, T json);

        public abstract string GetId(T json);

        public virtual void SetId(T json, string id, ResourceGroupProcessor processor)
        {
            throw new NotImplementedException($"SetId called on resource other than '{ResourceGroupProcessor.OrganizationStr}'");
        }

        public abstract string GetResourceType(T json);

        public abstract TS CreateClone(ResourceGroupProcessor processor, T originalJson, T cloneJson, int refSiblingNumber); // WARNING! originalJson MUST not be modified, member classes of originalJson MUST not be asigned to cloneJson!

        public abstract bool ValidateResourceRefsAndSelect(ResourceGroupProcessor processor, T json, out bool select);

        public int GetRefSiblingNumberLimit(ResourceGroupProcessor processor, T originalJson)
        {
            int refSiblingNumberLimit = int.MaxValue;
            IterateReferences(false, processor, originalJson, originalJson, -1, ref refSiblingNumberLimit);
            return refSiblingNumberLimit;
        }

        protected abstract void IterateReferences(bool clone, ResourceGroupProcessor processor, T originalJson, T cloneJson, int refSiblingNumber, ref int refSiblingNumberLimit);

        protected string CloneOrLimit(bool clone, T originalJson, string originalReference, int refSiblingNumber, ref int refSiblingNumberLimit)
        {
            string rgd = Processor.GetResourceGroupDir();
            string rt = GetResourceType(originalJson);
            string id = GetId(originalJson);

            int index = originalReference.IndexOf('/', StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FHIRDataSynthException(rgd, rt, id, $"Invalid reference {originalReference} in CloneOrLimit()."); // Should never happen!
            }

            string refType = originalReference.Substring(0, index);
            string refId = originalReference.Substring(index + 1);

            switch (refType)
            {
                /*case "AllergyIntolerance":
                    {
                    }
                case "CarePlan":
                    {
                    }*/
                case "CareTeam":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.CareTeams[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.CareTeams[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                case "Claim":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Claims[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Claims[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                /*case "Condition":
                    {
                    }
                case "Device":
                    {
                    }
                case "DiagnosticReport":
                    {
                    }*/
                case "Encounter":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Encounters[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Encounters[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                /*case "ExplanationOfBenefits":
                    {
                    }
                case "ImagingStudy":
                    {
                    }
                case "Immunization":
                    {
                    }
                case "MedicationAdministration":
                    {
                    }*/
                case "MedicationRequest":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.MedicationRequests[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.MedicationRequests[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                case "Observation":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Observations[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Observations[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                case "Organization":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Organizations[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Organizations[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                case "Patient":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Patients[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Patients[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                case "Practitioner":
                    {
                        if (clone)
                        {
                            return $"{refType}/{Processor.Practitioners[refId].Get(refSiblingNumber, rgd, rt, id).Id}";
                        }
                        else
                        {
                            refSiblingNumberLimit = Math.Min(Processor.Practitioners[refId].Count, refSiblingNumberLimit);
                        }

                        return originalReference;
                    }

                /*case "Procedure":
                    {
                    }
                case "SupplyDelivery":
                    {
                    }*/
                default: throw new FHIRDataSynthException(rgd, rt, id, $"Invalid reference type {originalReference} in CloneOrLimit()."); // Should never happen!
            }
        }

        // Enumerator.
        public virtual IEnumerator<EnumeratorItem> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public class EnumeratorItem
        {
            public int Size { get; set; }

            public T Json { get; set; }
        }

        public abstract class EnumeratorBase<TS1> : IEnumerator<EnumeratorItem>
        {
            private ResourceGroupProcessor processor;
            private JsonSerializerOptions options;
            private EnumeratorItem currentItem;
            private string line;

            public EnumeratorBase(ResourceGroupProcessor processor, JsonSerializerOptions options)
            {
                this.processor = processor;
                this.options = options;
                currentItem = new EnumeratorItem();
            }

            protected abstract TS1 InitializerCurrent { get; }

            public EnumeratorItem Current
            {
                get { return currentItem; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }

            protected abstract T LoadFHIRExampleFile();

            protected abstract void InitializeFHIRExample(T json, TS1 initializer);

            protected abstract bool InitializerMoveNext();

            public bool MoveNext()
            {
                if (!InitializerMoveNext())
                {
                    return false;
                }
                else
                {
                    TS1 initializer = InitializerCurrent;
                    if (currentItem.Json == null)
                    {
                        currentItem.Json = LoadFHIRExampleFile();
                        InitializeFHIRExample(currentItem.Json, initializer);
                        line = JsonSerializer.Serialize(currentItem.Json, options);
                        currentItem.Size = line.Length;
                    }
                    else
                    {
                        currentItem.Json = JsonSerializer.Deserialize<T>(line); // TODO: optimization, remove this deserialization by processing json item one at the time instead of loading them all into array.
                        InitializeFHIRExample(currentItem.Json, initializer);
                    }
                }

                return true;
            }

            public abstract void Reset();

            public abstract void Dispose();
        }
    }
}
