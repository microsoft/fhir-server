// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EnsureThat;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        private static readonly Regex ResourceIdValidationRegex = new Regex(
            "^[A-Za-z0-9\\-\\.]{1,64}$",
            RegexOptions.Compiled);

        private readonly IIgnixaJsonSerializer _serializer;
        private readonly IResourceWrapperFactory _resourceFactory;
        private readonly IIgnixaSchemaContext _schemaContext;

        public ImportResourceParser(
            IIgnixaJsonSerializer serializer,
            IResourceWrapperFactory resourceFactory,
            IIgnixaSchemaContext schemaContext)
        {
            _serializer = EnsureArg.IsNotNull(serializer, nameof(serializer));
            _resourceFactory = EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
            _schemaContext = EnsureArg.IsNotNull(schemaContext, nameof(schemaContext));
        }

        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            var resourceNode = _serializer.Parse(rawResource);
            ValidateResourceId(resourceNode?.Id);
            CheckConditionalReferenceInResource(resourceNode, importMode);

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resourceNode.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resourceNode.Meta.LastUpdated.Value;
            resourceNode.Meta.LastUpdated = new DateTimeOffset(lastUpdated.DateTime.TruncateToMillisecond(), lastUpdated.Offset);
            if (!lastUpdatedIsNull && resourceNode.Meta.LastUpdated.Value > Clock.UtcNow.AddSeconds(10)) // 5 sec is the max for the computers in the domain
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resourceNode.Meta.VersionId) || !int.TryParse(resourceNode.Meta.VersionId, out var _))
            {
                resourceNode.Meta.VersionId = "1";
                keepVersion = false;
            }

            var ignixaElement = new IgnixaResourceElement(resourceNode, _schemaContext.Schema);

            var isDeleted = ignixaElement.IsSoftDeleted();

            if (isDeleted)
            {
                RemoveSoftDeletedExtension(resourceNode);
            }

            // Use the extension method to create the wrapper directly from IgnixaResourceElement
            var resourceWrapper = _resourceFactory.Create(ignixaElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWrapper);
        }

        private void CheckConditionalReferenceInResource(ResourceJsonNode resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad)
            {
                return;
            }

            // Create IgnixaResourceElement for FhirPath evaluation
            var ignixaElement = new IgnixaResourceElement(resource, _schemaContext.Schema);

            // Use IReferenceMetadataProvider to identify reference fields for this resource type
            var referenceMetadata = _schemaContext.ReferenceMetadataProvider.GetMetadata(resource.ResourceType);
            foreach (var field in referenceMetadata)
            {
                // Strip [x] suffix for choice types - FhirPath handles polymorphic fields
                var elementPath = field.ElementPath.EndsWith("[x]", StringComparison.Ordinal)
                    ? field.ElementPath.Substring(0, field.ElementPath.Length - 3)
                    : field.ElementPath;

                // Use FhirPath to check if any reference contains '?' (conditional reference)
                // FhirPath naturally handles collections and nested paths
                var fhirPath = $"{elementPath}.reference.contains('?')";
                if (ignixaElement.Predicate(fhirPath))
                {
                    throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
                }
            }
        }

        private static void RemoveSoftDeletedExtension(ResourceJsonNode resource)
        {
            var metaNode = resource.MutableNode["meta"];
            if (metaNode is not JsonObject metaObject)
            {
                return;
            }

            if (!metaObject.TryGetPropertyValue("extension", out var extensionNode) || extensionNode is not JsonArray extensionArray)
            {
                return;
            }

            // Remove the soft-deleted extension
            for (int i = extensionArray.Count - 1; i >= 0; i--)
            {
                if (extensionArray[i] is JsonObject extensionObj &&
                    extensionObj.TryGetPropertyValue("url", out var urlNode) &&
                    urlNode is JsonValue urlValue)
                {
                    var url = urlValue.GetValue<string>();
                    if (string.Equals(url, KnownFhirPaths.AzureSoftDeletedExtensionUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        extensionArray.RemoveAt(i);
                    }
                }
            }

            // Clean up empty extension array
            if (extensionArray.Count == 0)
            {
                metaObject.Remove("extension");
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
