// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa.Features.Operations.Import
{
    /// <summary>
    /// Ignixa based implementation of <see cref="IImportResourceParser"/> that parses raw NDJSON
    /// resource content into <see cref="ImportResource"/> instances for the $import operation.
    /// </summary>
    public sealed class IgnixaImportResourceParser : IImportResourceParser
    {
        private readonly IResourceWrapperFactory _resourceFactory;
        private readonly IgnixaSchemaContext _schemaContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaImportResourceParser"/> class.
        /// </summary>
        /// <param name="resourceFactory">The factory used to create resource wrappers.</param>
        /// <param name="schemaContext">The Ignixa generated schema for the current FHIR version.</param>
        public IgnixaImportResourceParser(IResourceWrapperFactory resourceFactory, IgnixaSchemaContext schemaContext)
        {
            EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
            EnsureArg.IsNotNull(schemaContext, nameof(schemaContext));

            _resourceFactory = resourceFactory;
            _schemaContext = schemaContext;
        }

        /// <inheritdoc />
        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            ResourceJsonNode resource;
            try
            {
                resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(rawResource);
            }
            catch (JsonException exception)
            {
                throw new FormatException("Failed to parse import resource JSON.", exception);
            }

            ImportResourceIdValidator.Validate(resource.Id);
            CheckConditionalReferenceInResource(resource, importMode);

            resource.Meta ??= new MetaJsonNode();

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resource.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resource.Meta.LastUpdated.Value;
            resource.Meta.LastUpdated = new DateTimeOffset(lastUpdated.DateTime.TruncateToMillisecond(), lastUpdated.Offset);
            if (!lastUpdatedIsNull && resource.Meta.LastUpdated.Value > Clock.UtcNow.AddSeconds(10)) // 10 sec is the max for the computers in the domain
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resource.Meta.VersionId) || !int.TryParse(resource.Meta.VersionId, out var _))
            {
                resource.Meta.VersionId = "1";
                keepVersion = false;
            }

            // Phase-1 flip point: the one-arg ResourceElement ctor below leaves ResourceInstance unset, so
            // RawResourceFactory can't see the native ResourceJsonNode and falls through to a full ToPoco<T>()
            // rebuild plus Firely's FhirJsonSerializer - the same cost Firely mode pays, on top of the Ignixa
            // parse above. The next phase should carry the node through via the two-arg ResourceElement ctor
            // and add a native-serialize IRawResourceFactory decorator that uses it when present; that's the
            // biggest single perf win per the sdk-migration import-performance-analysis doc. Don't just swap
            // the ctor here without adding that decorator in the same change, or nothing downstream will use it.
            var resourceElement = new ResourceElement(resource.ToElement(_schemaContext.Schema).ToTypedElement());
            var isDeleted = resourceElement.IsSoftDeleted();

            if (isDeleted)
            {
                // ResourceJsonNode caches its converted IElement internally (per node instance), so mutating
                // resource.MutableNode and calling ToElement() again on the *same* node would silently return
                // the stale pre-mutation element. Re-parse a fresh node from the mutated JSON instead.
                RemoveSoftDeletedExtension(resource.MutableNode);
                resource = JsonSourceNodeFactory.Parse<ResourceJsonNode>(resource.MutableNode.ToJsonString());

                // Same phase-1 flip point as above.
                resourceElement = new ResourceElement(resource.ToElement(_schemaContext.Schema).ToTypedElement());
            }

            var resourceWrapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWrapper);
        }

        /// <summary>
        /// Rejects conditional references (a "reference" value containing '?') found in the fields the
        /// generated Ignixa schema declares as Reference-typed for this resource's own type.
        /// </summary>
        /// <remarks>
        /// Scoped to the resource's direct, schema-declared reference fields only — matching
        /// <see cref="Microsoft.Health.Fhir.Core.Features.Search.TypedElementSearchIndexer"/>, which likewise
        /// never indexes into <c>contained</c> resources. Bundle entries are out of scope for $import: the
        /// generated schema has no reference metadata for <c>Bundle</c> itself (each entry is a distinct,
        /// independently-typed resource, not a Reference-typed field of Bundle), and import NDJSON is expected
        /// to contain individual resources rather than transactional Bundles.
        /// </remarks>
        private void CheckConditionalReferenceInResource(ResourceJsonNode resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad || resource.MutableNode is not JsonObject root)
            {
                return;
            }

            foreach (ReferenceFieldMetadata field in _schemaContext.Schema.ReferenceMetadataProvider.GetMetadata(resource.ResourceType))
            {
                var propertyName = field.ElementPath.EndsWith("[x]", StringComparison.Ordinal)
                    ? string.Concat(field.ElementPath.AsSpan(0, field.ElementPath.Length - 3), "Reference")
                    : field.ElementPath;

                if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
                {
                    continue;
                }

                if (field.IsCollection)
                {
                    if (value is JsonArray array)
                    {
                        foreach (var item in array)
                        {
                            ThrowIfConditionalReference(item);
                        }
                    }
                }
                else
                {
                    ThrowIfConditionalReference(value);
                }
            }
        }

        private static void ThrowIfConditionalReference(JsonNode referenceNode)
        {
            if (referenceNode is JsonObject referenceObject &&
                referenceObject.TryGetPropertyValue("reference", out var referenceValue) &&
                referenceValue is JsonValue jsonValue &&
                jsonValue.TryGetValue(out string reference) &&
                reference.Contains('?', StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
            }
        }

        /// <summary>
        /// Removes every extension whose URL matches <see cref="KnownFhirPaths.AzureSoftDeletedExtensionUrl"/>,
        /// removing the now-empty extension array if applicable. Only called once <see cref="ResourceElement"/>'s
        /// FHIRPath-driven <c>IsSoftDeleted()</c> predicate (the same one the Firely parser uses) has already
        /// confirmed the resource is soft-deleted, so — mirroring Firely's <c>Meta.RemoveExtension</c> — every
        /// extension matching the URL is removed regardless of its value.
        /// </summary>
        /// <param name="root">The raw JSON graph for the resource.</param>
        private static void RemoveSoftDeletedExtension(JsonNode root)
        {
            if (root?["meta"] is not JsonObject meta || meta["extension"] is not JsonArray extensions)
            {
                return;
            }

            for (var i = extensions.Count - 1; i >= 0; i--)
            {
                if (extensions[i] is JsonObject extension &&
                    extension["url"]?.GetValue<string>() is string url &&
                    string.Equals(url, KnownFhirPaths.AzureSoftDeletedExtensionUrl, StringComparison.Ordinal))
                {
                    extensions.RemoveAt(i);
                }
            }

            if (extensions.Count == 0)
            {
                meta.Remove("extension");
            }
        }
    }
}
