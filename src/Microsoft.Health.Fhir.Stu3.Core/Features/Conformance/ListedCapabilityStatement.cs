// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public sealed class ListedCapabilityStatement : IListedCapabilityStatement
    {
        public Uri Url { get; set; }

        public string Id { get; set; }

        public string Version { get; set; }

        public string Name { get; set; }

        public IList<PublicationStatus> Status { get; set; }

        public bool Experimental { get; set; }

        public string Publisher { get; set; }

        public IList<ListedContactPoint> Telecom { get; set; }

        public IList<CapabilityStatementKind> Kind { get; set; }

        public SoftwareComponent Software { get; set; }

        public string FhirVersion { get; set; }

        public IList<UnknownContentCode> AcceptUnknown { get; set; }

        public IList<string> Format { get; set; }

        public IList<ListedRestComponent> Rest { get; set; }

        public IList<Code<PublicationStatus>> StatusElement { get; set; }

        public void TryAddRestInteraction(string resourceType, string value)
        {
            EnsureArg.IsNotNullOrEmpty(value, nameof(value));
            EnsureArg.IsTrue(Enum.TryParse<ResourceType>(resourceType, out var resource), nameof(resourceType));

            var interaction = value.GetValueByEnumLiteral<TypeRestfulInteraction>();

            this.TryAddRestInteraction(resource, interaction);
        }

        public void TryAddRestInteraction(string interaction)
        {
            EnsureArg.IsNotNullOrEmpty(interaction, nameof(interaction));

            var systemInteraction = interaction.GetValueByEnumLiteral<SystemRestfulInteraction>();

            var restComponent = Rest.Single();

            if (restComponent.Interaction == null)
            {
                restComponent.Interaction = new List<SystemInteractionComponent>();
            }

            restComponent.Interaction.Add(new SystemInteractionComponent
            {
                Code = systemInteraction,
            });
        }

        public void BuildRestResourceComponent(string resourceType, Action<IListedResourceComponent> action)
        {
            EnsureArg.IsTrue(Enum.TryParse<ResourceType>(resourceType, out var resource), nameof(resourceType));

            this.BuildRestResourceComponent(resource, action);
        }
    }
}
