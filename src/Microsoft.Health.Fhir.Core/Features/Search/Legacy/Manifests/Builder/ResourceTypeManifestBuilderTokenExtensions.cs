// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public static class ResourceTypeManifestBuilderTokenExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> systemSelector,
            Func<TCollection, string> codeSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new TokenExtractor<TResource, TCollection>(
                collectionSelector,
                systemSelector,
                codeSelector);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> codeSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new TokenExtractor<TResource, TCollection>(
                collectionSelector,
                item => string.Empty,
                codeSelector);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, IEnumerable<Coding>> codingSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new TokenExtractor<TResource, Coding>(
                resource => collectionSelector.ExtractNonEmptyCollection(resource).SelectMany(codingSelector.ExtractNonEmptyCoding),
                coding => coding.System,
                coding => coding.Code,
                coding => coding.Display);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<Coding>> codingSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.AddTokenSearchParam(
                paramName,
                resource => Enumerable.Repeat(resource, 1),
                codingSelector);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, IEnumerable<CodeableConcept>> codeableConceptsSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(codeableConceptsSelector, nameof(codeableConceptsSelector));

            // Based on spec: http://hl7.org/fhir/search.html#token,
            // the text for CodeableConcept is specified by CodeableConcept.text or CodeableConcept.Coding.display.
            var extractor = new TokenExtractor<TResource, Coding>(
                resource =>
                {
                    List<Coding> codings = new List<Coding>();

                    IEnumerable<CodeableConcept> codeableConcepts = collectionSelector.ExtractNonEmptyCollection(resource)
                        .SelectMany(item => codeableConceptsSelector(item))
                        .Where(item => item != null);

                    foreach (CodeableConcept cc in codeableConcepts)
                    {
                        // First check for the codeable concept text.
                        if (!string.IsNullOrWhiteSpace(cc.Text))
                        {
                            codings.Add(new Coding(null, null, cc.Text));
                        }

                        // Then check for each coding.
                        if (cc.Coding != null)
                        {
                            codings.AddRange(cc.Coding.Where(coding => coding != null && !coding.IsEmpty()));
                        }
                    }

                    return codings;
                },
                coding => coding.System,
                coding => coding.Code,
                coding => coding.Display);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> codeableConceptSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(codeableConceptSelector, nameof(codeableConceptSelector));

            return builder.AddTokenSearchParam(
                paramName,
                collectionSelector,
                resource => Enumerable.Repeat(codeableConceptSelector(resource), 1));
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<CodeableConcept>> codeableConceptsSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.AddTokenSearchParam(
                paramName,
                resource => codeableConceptsSelector(resource),
                codeableConcept => codeableConcept);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, CodeableConcept> codeableConceptSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.AddTokenSearchParam(
                paramName,
                resource => Enumerable.Repeat(resource, 1),
                codeableConceptSelector);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, string> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            // Token search params that only extract a string should have a null system.
            // When this is the case, the search logic will match on the coding value where a system has not been defined.
            // http://hl7.org/fhir/search.html#token

            var extractor = new TokenExtractor<TResource, string>(
                resource =>
                {
                    string value = stringSelector(resource);

                    return string.IsNullOrEmpty(value) ? Enumerable.Empty<string>() : Enumerable.Repeat(value, 1);
                },
                s => null,
                s => s);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Javascript representation of bool")]
        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, bool?> boolSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(boolSelector, nameof(boolSelector));

            var extractor = new TokenExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                r => string.Empty,
                r =>
                {
                    bool? value = boolSelector(r);

                    if (value == null)
                    {
                        return null;
                    }

                    return value.Value ?
                        bool.TrueString.ToLowerInvariant() :
                        bool.FalseString.ToLowerInvariant();
                });

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Enum> enumSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(enumSelector, nameof(enumSelector));

            var extractor = new TokenExtractor<TResource, TCollection>(
                collectionSelector,
                e => enumSelector(e)?.GetSystem(),
                e => enumSelector(e)?.GetLiteral());

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Enum> enumSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(enumSelector, nameof(enumSelector));

            var extractor = new TokenExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                e => enumSelector(e)?.GetSystem(),
                e => enumSelector(e)?.GetLiteral());

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<ContactPoint>> contactPointsSelector,
            ContactPoint.ContactPointSystem? contactPointSystemFilter = null)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(contactPointsSelector, nameof(contactPointsSelector));

            var extractor = new TokenExtractor<TResource, ContactPoint>(
                resource =>
                {
                    return contactPointsSelector(resource)?
                        .Where(
                            item => item != null &&
                            item.System != null &&
                            item.System == (contactPointSystemFilter ?? item.System) &&
                            !string.IsNullOrEmpty(item.Value)) ??
                        Enumerable.Empty<ContactPoint>();
                },
                cp => cp.Use?.GetLiteral(),
                cp => cp.Value);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<Identifier>> identifiersSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(identifiersSelector, nameof(identifiersSelector));

            // Based on spec: http://hl7.org/fhir/search.html#token,
            // the text for identifier is specified by Identifier.type.text.
            var extractor = new TokenExtractor<TResource, Identifier>(
                identifiersSelector,
                identifier => identifier.System,
                identifier => identifier.Value,
                identifier => identifier.Type?.Text);

            return builder.AddTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Identifier> identifiersSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(identifiersSelector, nameof(identifiersSelector));

            var extractor = new TokenExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                identifier => identifiersSelector(identifier)?.System,
                identifier => identifiersSelector(identifier)?.Value);

            return builder.AddTokenSearchParam(paramName, extractor);
        }
    }
}
