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
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
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
        private const int MaxSoftDeleteExtensionRemovals = 1000;

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
                throw new FormatException($"Failed to parse import resource JSON: {exception.Message}", exception);
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

            var element = resource.ToElement(_schemaContext.Schema);
            var isDeleted = IsSoftDeleted(element);

            if (isDeleted)
            {
                // SourceNodeExtensions.RemoveExtension only removes one matching extension per call - loop
                // until none remain, mirroring Firely's Meta.RemoveExtension (which removes every extension
                // matching the URL, regardless of value). Each call rescans the (shrinking) extension array
                // from the start, so this is O(n^2) in the number of matching extensions; MaxSoftDeleteExtensionRemovals
                // bounds the cost against a resource crafted with a pathological number of duplicates.
                var softDeleteExtensionsRemoved = 0;
                while (SourceNodeExtensions.RemoveExtension(resource.Meta, KnownFhirPaths.AzureSoftDeletedExtensionUrl))
                {
                    if (++softDeleteExtensionsRemoved > MaxSoftDeleteExtensionRemovals)
                    {
                        throw new FormatException(
                            $"Resource has more than {MaxSoftDeleteExtensionRemovals} extensions matching the soft-deleted URL.");
                    }
                }

                // ResourceJsonNode caches its converted IElement internally (per node instance); InvalidateCaches()
                // clears that cache so the next ToElement() call reflects the mutation above instead of silently
                // returning the stale pre-mutation element.
                resource.InvalidateCaches();
                element = resource.ToElement(_schemaContext.Schema);
            }

            // Phase-2a flip point: the one-arg ResourceElement ctor below leaves ResourceInstance unset, so
            // RawResourceFactory can't see the native ResourceJsonNode and falls through to a full ToPoco<T>()
            // rebuild plus Firely's FhirJsonSerializer - the same cost Firely mode pays, on top of the Ignixa
            // parse above. The next phase should carry the node through via the two-arg ResourceElement ctor
            // and add a native-serialize IRawResourceFactory decorator that uses it when present; that's the
            // biggest single perf win per the sdk-migration import-performance-analysis doc. Don't just swap
            // the ctor here without adding that decorator in the same change, or nothing downstream will use it.
            var resourceElement = new ResourceElement(element.ToTypedElement());
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
                // Choice-type fields typed as Reference by the schema (e.g. Extension.value[x]) serialize with
                // the concrete type name suffixed in JSON (valueReference), not the schema's "[x]" placeholder.
                var propertyName = field.ElementPath.EndsWith("[x]", StringComparison.Ordinal)
                    ? string.Concat(field.ElementPath.AsSpan(0, field.ElementPath.Length - 3), "Reference")
                    : field.ElementPath;

                if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
                {
                    continue;
                }

                if (field.IsCollection && value is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        ThrowIfConditionalReference(item, resource.FhirVersion);
                    }
                }
                else
                {
                    // Firely's parser is configured with PermissiveParsing (FhirModule.cs) and tolerates a lone
                    // object where a 0..* field expects an array, treating it as a single-element collection.
                    // Match that leniency here (field.IsCollection but value isn't a JsonArray) instead of
                    // rejecting it - ThrowIfConditionalReference still throws below if this value isn't even
                    // a JSON object.
                    ThrowIfConditionalReference(value, resource.FhirVersion);
                }
            }
        }

        /// <summary>
        /// Reads the reference field through the typed <see cref="ReferenceJsonNode"/> model instead of casting
        /// through raw <see cref="JsonValue"/>. A missing "reference" property (e.g. an identifier-only or
        /// display-only reference, both valid FHIR) yields a null <see cref="ReferenceJsonNode.Reference"/> and
        /// is skipped, matching the Firely parser. A non-string "reference" scalar (e.g. <c>"reference": 123</c>)
        /// is deliberately not guarded against - <see cref="ReferenceJsonNode.Reference"/> throws in that case.
        /// A reference field that is present but isn't a JSON object at all (schema-invalid, e.g. a bare string
        /// or number) also throws here rather than being silently skipped - confirmed empirically that
        /// <c>resource.ToElement(schema)</c> does NOT reject this shape on its own, so this is the only place
        /// that catches it. A null array item (e.g. <c>[null, {...}]</c>) is treated as absent, not malformed.
        /// </summary>
        private static void ThrowIfConditionalReference(JsonNode referenceNode, FhirVersion? fhirVersion)
        {
            if (referenceNode is null)
            {
                return;
            }

            if (referenceNode is not JsonObject referenceObject)
            {
                throw new FormatException($"Expected a Reference object but found {referenceNode.GetValueKind()}.");
            }

            var reference = new ReferenceJsonNode(referenceObject, fhirVersion).Reference;
            if (!string.IsNullOrWhiteSpace(reference) && reference.Contains('?', StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
            }
        }

        /// <summary>
        /// Evaluates the same soft-delete predicate the Firely parser uses via <c>ResourceElement.IsSoftDeleted()</c>
        /// (<see cref="KnownFhirPaths.IsSoftDeletedExtension"/>), but directly against the native Ignixa element
        /// through Ignixa's own FHIRPath engine - no Firely adapter involved for this check.
        /// </summary>
        private static bool IsSoftDeleted(IElement element)
        {
            var context = new EvaluationContext { Resource = element, RootResource = element };
            return element.Predicate(KnownFhirPaths.IsSoftDeletedExtension, context);
        }
    }
}
