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
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Provides agnostic access to FHIR Models and Resources
    /// </summary>
    public partial class VersionSpecificModelInfoProvider : IModelInfoProvider
    {
        public VersionInfo SupportedVersion { get; } = new VersionInfo(ModelInfo.Version);

        public IStructureDefinitionSummaryProvider StructureDefinitionSummaryProvider { get; } = new PocoStructureDefinitionSummaryProvider();

        public string GetFhirTypeNameForType(Type type)
        {
            return ModelInfo.GetFhirTypeNameForType(type);
        }

        public bool IsKnownResource(string name)
        {
            return ModelInfo.IsKnownResource(name);
        }

        public bool IsKnownCompartmentType(string compartmentType)
        {
            return Enum.IsDefined(typeof(CompartmentType), compartmentType);
        }

        public IReadOnlyCollection<string> GetResourceTypeNames()
        {
            List<string> supportedResources = ModelInfo.SupportedResources;

            if (Version == FhirSpecification.R5)
            {
                supportedResources = supportedResources.Where(x => x != "CanonicalResource" && x != "MetadataResource" && x != "Citation").ToList();
            }

            return supportedResources;
        }

        public IReadOnlyCollection<string> GetCompartmentTypeNames()
        {
            return Enum.GetNames(typeof(CompartmentType));
        }

        public Type GetTypeForFhirType(string resourceType)
        {
            return ModelInfo.GetTypeForFhirType(resourceType);
        }

        public EvaluationContext GetEvaluationContext(Func<string, ITypedElement> elementResolver = null)
        {
            return new FhirEvaluationContext
            {
                ElementResolver = elementResolver,
            };
        }

        public ITypedElement ToTypedElement(ISourceNode sourceNode)
        {
            EnsureArg.IsNotNull(sourceNode);

            return sourceNode.ToTypedElement(StructureDefinitionSummaryProvider);
        }

        public ITypedElement ToTypedElement(RawResource rawResource)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            using TextReader reader = new StringReader(rawResource.Data);
            using JsonReader jsonReader = new JsonTextReader(reader);
            try
            {
                ISourceNode sourceNode = FhirJsonNode.Read(jsonReader);
                return sourceNode.ToTypedElement(StructureDefinitionSummaryProvider);
            }
            catch (FormatException ex)
            {
                var issue = new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Fatal,
                    OperationOutcomeConstants.IssueType.Invalid,
                    ex.Message);

                throw new ResourceNotValidException(new List<OperationOutcomeIssue>() { issue });
            }
        }
    }
}
