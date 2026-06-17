// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ListedCapabilityStatement
    {
        internal const string ServerMode = "server";

        public ListedCapabilityStatement()
        {
            Id = new ResourceIdProvider().Create();
            Status = new DefaultOptionHashSet<string>("draft", StringComparer.Ordinal);
            Kind = new DefaultOptionHashSet<string>("capability", StringComparer.Ordinal);
            Rest = new ThreadSafeHashSet<ListedRestComponent>(new PropertyEqualityComparer<ListedRestComponent>(x => x.Mode));
            Format = new ThreadSafeHashSet<string>(StringComparer.Ordinal);
            PatchFormat = new ThreadSafeHashSet<string>(StringComparer.Ordinal);
            AdditionalData = new ConcurrentDictionary<string, JToken>();
            Profile = new ThreadSafeList<ReferenceComponent>();
        }

        public string ResourceType { get; } = "CapabilityStatement";

        public Uri Url { get; set; }

        public string Id { get; set; }

        public string Version { get; set; }

        public string Name { get; set; }

        public ICollection<string> Status { get; }

        public bool Experimental { get; set; }

        public string Publisher { get; set; }

        public ICollection<string> Kind { get; }

        public SoftwareComponent Software { get; set; }

        public string Date { get; set; }

        public string FhirVersion { get; set; }

        public ICollection<string> Format { get; }

        public ICollection<string> PatchFormat { get; }

        public ICollection<ListedRestComponent> Rest { get; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; }

        public ICollection<ReferenceComponent> Profile { get; }

        public ICollection<string> Instantiates { get; internal set; }

        public ListedCapabilityStatement Clone()
        {
            var clone = new ListedCapabilityStatement
            {
                Url = Url,
                Id = Id,
                Version = Version,
                Name = Name,
                Experimental = Experimental,
                Publisher = Publisher,
                Software = new SoftwareComponent()
                {
                    Name = Software?.Name,
                    Version = Software?.Version,
                },
                Date = Date,
                FhirVersion = FhirVersion,
            };

            SafeCopyTo(Status, clone.Status);

            SafeCopyTo(Kind, clone.Kind);

            SafeCopyTo(Format, clone.Format);

            SafeCopyTo(PatchFormat, clone.PatchFormat);

            SafeCopyTo(Rest, clone.Rest);

            SafeCopyTo(Profile, clone.Profile);

            SafeCopyTo(AdditionalData, clone.AdditionalData);

            SafeCopyTo(Instantiates, clone.Instantiates);

            return clone;
        }

        /// <summary>
        /// Safe way to copy data from a collection to another avoiding concurrent changes in the origin to raise errors in production.
        /// </summary>
        /// <typeparam name="T">Generic data type being copied.</typeparam>
        /// <param name="origin">Origin.</param>
        /// <param name="destiny">Destiny.</param>
        private static void SafeCopyTo<T>(ICollection<T> origin, ICollection<T> destiny)
        {
            if (origin == null)
            {
                destiny = null;
                return;
            }

            T[] temp = new T[origin.Count];
            origin.CopyTo(temp, 0);

            foreach (T item in temp)
            {
                destiny.Add(item);
            }
        }
    }
}
