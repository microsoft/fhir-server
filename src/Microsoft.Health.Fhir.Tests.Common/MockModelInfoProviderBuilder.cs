// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using NSubstitute;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class MockModelInfoProviderBuilder
    {
        private readonly IModelInfoProvider _mock;
        private readonly HashSet<string> _knownTypes;

        private MockModelInfoProviderBuilder(IModelInfoProvider mock, HashSet<string> knownTypes)
        {
            EnsureArg.IsNotNull(mock, nameof(mock));
            EnsureArg.IsNotNull(knownTypes, nameof(knownTypes));

            _mock = mock;
            _knownTypes = knownTypes;
        }

        public static MockModelInfoProviderBuilder Create(FhirSpecification version)
        {
            IModelInfoProvider provider = Substitute.For<IModelInfoProvider>();
            provider.Version.ReturnsForAnyArgs(version);

            // Adds normative types by default
            var seenTypes = new HashSet<string>
            {
                "Binary", "Bundle", "CapabilityStatement",  "CodeSystem", "Observation", "OperationOutcome", "Patient", "StructureDefinition", "ValueSet",
            };

            provider.GetResourceTypeNames().Returns(_ => seenTypes.Where(x => !string.IsNullOrEmpty(x)).ToArray());
            provider.IsKnownResource(Arg.Any<string>()).Returns(x => provider.GetResourceTypeNames().Contains(x[0]));

            // Simulate inherited behavior
            // Some code depends on "InheritedResource".BaseType
            // This adds the ability to resolve "Resource" as the base type
            provider.GetTypeForFhirType(Arg.Any<string>()).Returns(p => p.ArgAt<string>(0) == "Resource" ? typeof(ResourceObj) : typeof(InheritedResourceObj));
            provider.GetFhirTypeNameForType(Arg.Any<Type>()).Returns(p => p.ArgAt<Type>(0) == typeof(ResourceObj) ? "Resource" : null);

            // IStructureDefinitionSummaryProvider allows the execution of FHIRPath queries
            provider.ToTypedElement(Arg.Any<ISourceNode>())
                .Returns(p => p.ArgAt<ISourceNode>(0).ToTypedElement(new MockStructureDefinitionSummaryProvider(p.ArgAt<ISourceNode>(0), seenTypes)));

            provider.ToTypedElement(Arg.Any<RawResource>())
                .Returns(r =>
                {
                    using TextReader reader = new StringReader(r.ArgAt<RawResource>(0).Data);
                    using JsonReader jsonReader = new JsonTextReader(reader);
                    ISourceNode sourceNode = FhirJsonNode.Read(jsonReader);
                    return sourceNode.ToTypedElement(new MockStructureDefinitionSummaryProvider(sourceNode, seenTypes));
                });

            return new MockModelInfoProviderBuilder(provider, seenTypes);
        }

        public MockModelInfoProviderBuilder AddKnownTypes(params string[] knownResourceTypes)
        {
            EnsureArg.IsNotNull(knownResourceTypes, nameof(knownResourceTypes));

            foreach (var item in knownResourceTypes)
            {
                _knownTypes.Add(item);
            }

            return this;
        }

        public IModelInfoProvider Build()
        {
            return _mock;
        }

        private class ResourceObj
        {
        }

        private class InheritedResourceObj : ResourceObj
        {
        }
    }
}
