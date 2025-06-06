// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SourceNodeSerialization;
using Microsoft.Health.Fhir.SourceNodeSerialization.Extensions;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        private static readonly Regex ResourceIdValidationRegex = new Regex(
            "^[A-Za-z0-9\\-\\.]{1,64}$",
            RegexOptions.Compiled);

        private FhirJsonParser _parser;
        private IResourceWrapperFactory _resourceFactory;

        public ImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory)
        {
            _parser = EnsureArg.IsNotNull(parser, nameof(parser));
            _resourceFactory = EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
        }

        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            var resource = ResourceJsonNode.Parse(rawResource);

            ValidateResourceId(resource?.Id);

            if (resource.Meta == null)
            {
                resource.Meta = new MetaJsonNode();
            }

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resource.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resource.Meta.LastUpdated;
            var updatedDateTime = new DateTimeOffset(lastUpdated.Value.DateTime.TruncateToMillisecond(), lastUpdated.Value.Offset);
            resource.Meta.LastUpdated = updatedDateTime;

            if (!lastUpdatedIsNull && updatedDateTime > Clock.UtcNow.AddSeconds(10)) // 5 sec is the max for the computers in the domain
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resource.Meta.VersionId) || !int.TryParse(resource.Meta.VersionId, out var _))
            {
                resource.Meta.VersionId = "1";
                keepVersion = false;
            }

            // Returns true if the extension was removed, false if it was not present.
            var isDeleted = resource.Meta.RemoveExtension(KnownFhirPaths.AzureSoftDeletedExtensionUrl);

            var resourceElement = resource
                .ToResourceElement(ModelInfoProvider.StructureDefinitionSummaryProvider);

            CheckConditionalReferenceInResourceJsonNode(resourceElement, importMode);

            var resourceWapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWapper);
        }

        private static void CheckConditionalReferenceInResourceJsonNode(ResourceElement resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad)
            {
                return;
            }

            IEnumerable<(string Path, string ReferenceValue)> references = resource.Instance.GetReferenceValues();
            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.ReferenceValue))
                {
                    continue;
                }

                if (reference.ReferenceValue.Contains('?', StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
                }
            }
        }

        private static void ValidateResourceId(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId) || !ResourceIdValidationRegex.IsMatch(resourceId))
            {
                throw new BadRequestException($"Invalid resource id: '{resourceId ?? "null or empty"}'. " + Core.Resources.IdRequirements);
            }
        }
    }
}
