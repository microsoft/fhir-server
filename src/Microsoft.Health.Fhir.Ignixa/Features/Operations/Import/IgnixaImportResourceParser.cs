// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnsureThat;
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
        /// The extension <c>value[x]</c> property names whose FHIR primitive type maps to the FHIRPath
        /// <c>System.String</c> type, and therefore compares equal to a string literal (e.g. <c>"soft-deleted"</c>)
        /// under FHIRPath's <c>=</c> operator. Confirmed empirically against the Firely SDK for STU3/R4/R4B/R5:
        /// <c>string</c>, <c>code</c>, <c>id</c>, <c>markdown</c>, <c>uri</c>, <c>url</c>, <c>canonical</c>,
        /// <c>oid</c>, and <c>uuid</c> all satisfy <c>value='soft-deleted'</c> when their content is exactly
        /// <c>"soft-deleted"</c>; <c>boolean</c>, <c>integer</c>, <c>decimal</c>, <c>dateTime</c>, <c>instant</c>,
        /// and <c>base64Binary</c> never do (base64Binary additionally fails to parse for non-base64 content).
        /// </summary>
        private static readonly string[] StringLikeSoftDeleteValuePropertyNames =
        {
            "valueString",
            "valueCode",
            "valueId",
            "valueMarkdown",
            "valueUri",
            "valueUrl",
            "valueCanonical",
            "valueOid",
            "valueUuid",
        };

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
            CheckConditionalReferenceInResource(resource.MutableNode, importMode);

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

            var isDeleted = RemoveSoftDeletedExtension(resource.MutableNode);

            var element = resource.ToElement(_schemaContext.Schema);
            var resourceElement = new ResourceElement(element.ToTypedElement());

            var resourceWrapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWrapper);
        }

        /// <summary>
        /// Recursively walks the raw JSON graph — including contained resources and Bundle entries —
        /// rejecting conditional references (a "reference" value containing '?') during an initial load.
        /// </summary>
        private static void CheckConditionalReferenceInResource(JsonNode node, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad || node is null)
            {
                return;
            }

            Visit(node);

            static void Visit(JsonNode current)
            {
                if (current is JsonObject jsonObject)
                {
                    foreach (var property in jsonObject)
                    {
                        if (string.Equals(property.Key, "reference", StringComparison.Ordinal) &&
                            property.Value is JsonValue jsonValue &&
                            jsonValue.TryGetValue(out string reference) &&
                            reference.Contains('?', StringComparison.Ordinal))
                        {
                            throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
                        }

                        if (property.Value is not null)
                        {
                            Visit(property.Value);
                        }
                    }
                }
                else if (current is JsonArray jsonArray)
                {
                    foreach (var item in jsonArray)
                    {
                        if (item is not null)
                        {
                            Visit(item);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Detects the Azure soft-deleted meta extension and, if present, removes every extension whose URL
        /// matches <see cref="KnownFhirPaths.AzureSoftDeletedExtensionUrl"/>, removing the now-empty extension
        /// array if applicable.
        /// </summary>
        /// <remarks>
        /// Mirrors the Firely parser's <c>ResourceElement.IsSoftDeleted()</c> FHIRPath predicate
        /// (<see cref="KnownFhirPaths.IsSoftDeletedExtension"/>: <c>value='soft-deleted'</c>), which requires an
        /// exact (case-sensitive) URL match AND a polymorphic <c>value[x]</c> that FHIRPath considers string-equal
        /// to <c>"soft-deleted"</c> before the resource is considered deleted. Because <c>value[x]</c> is
        /// polymorphic, the FHIRPath comparison succeeds for any FHIR primitive whose type maps to the FHIRPath
        /// <c>System.String</c> type (see <see cref="StringLikeSoftDeleteValuePropertyNames"/>), not just
        /// <c>valueString</c> - confirmed empirically for all four supported FHIR versions. Extensions that only
        /// match the URL (wrong/missing value, non-string-like value type, or a differently-cased URL) are left
        /// untouched, matching Firely's behavior of not removing extensions unless the resource is actually
        /// determined to be soft-deleted. Once soft-deleted is confirmed, every extension with the matching URL is
        /// removed (mirroring Firely's Meta.RemoveExtension, which removes all matches by URL regardless of
        /// value), so duplicate/invalid-valued extensions sharing the same URL are removed alongside the
        /// canonical one.
        /// </remarks>
        /// <param name="root">The raw JSON graph for the resource.</param>
        /// <returns><c>true</c> if a canonical soft-deleted extension was found and removed; otherwise, <c>false</c>.</returns>
        private static bool RemoveSoftDeletedExtension(JsonNode root)
        {
            if (root?["meta"] is not JsonObject meta || meta["extension"] is not JsonArray extensions)
            {
                return false;
            }

            var isDeleted = extensions.Any(extension =>
                extension is JsonObject extensionObject &&
                extensionObject["url"]?.GetValue<string>() is string url &&
                string.Equals(url, KnownFhirPaths.AzureSoftDeletedExtensionUrl, StringComparison.Ordinal) &&
                StringLikeSoftDeleteValuePropertyNames.Any(propertyName =>
                    extensionObject[propertyName]?.GetValue<string>() is string value &&
                    string.Equals(value, "soft-deleted", StringComparison.Ordinal)));

            if (!isDeleted)
            {
                return false;
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

            return true;
        }
    }
}
