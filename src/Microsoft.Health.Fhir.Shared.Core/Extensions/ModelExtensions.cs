﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ModelExtensions
    {
        /// <summary>
        /// This method provides temporary compatibility while STU3/R4 compatibility is added
        /// </summary>
        public static void SetModelInfoProvider()
        {
            ModelInfoProvider.SetProvider(new VersionSpecificModelInfoProvider());
        }

        public static ResourceElement ToResourceElement(this Base resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource.ToTypedElement().ToResourceElement();
        }

        public static ResourceElement ToResourceElement(this RawResourceElement resource, ResourceDeserializer deserializer)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource.ToPoco(deserializer).ToResourceElement();
        }

        public static T ToPoco<T>(this ResourceElement resource)
            where T : Resource
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return (T)resource.ResourceInstance ?? resource.Instance.ToPoco<T>();
        }

        public static Resource ToPoco(this ResourceElement resource)
        {
            return ToPoco<Resource>(resource);
        }

        public static T ToPoco<T>(this RawResourceElement resource, ResourceDeserializer deserializer)
            where T : Resource
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));

            var deserialized = deserializer.DeserializeRawResourceElement(resource);
            return deserialized.ToPoco<T>();
        }

        public static Resource ToPoco(this RawResourceElement resource, ResourceDeserializer deserializer)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));

            return resource.ToPoco<Resource>(deserializer);
        }

        public static ResourceElement UpdateId(this ResourceElement resource, string newId)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();
            poco.Id = newId;
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateVersion(this ResourceElement resource, string newVersion)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();
            poco.VersionId = newVersion;
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateLastUpdated(this ResourceElement resource, DateTimeOffset lastUpdated)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();
            poco.Meta.LastUpdated = lastUpdated;
            return poco.ToResourceElement();
        }

        public static ResourceElement TryAddSoftDeletedExtension(this ResourceElement resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource poco = resource.ToPoco();
            poco.Meta ??= new Meta();

            if (!poco.Meta.Extension.Any(x => string.Equals(x.Url, KnownFhirPaths.AzureSoftDeletedExtensionUrl, StringComparison.OrdinalIgnoreCase)))
            {
                poco.Meta.Extension.Add(
                    new Extension
                    {
                        Url = KnownFhirPaths.AzureSoftDeletedExtensionUrl,
                        Value = new FhirString("soft-deleted"),
                    });
            }

            return poco.ToResourceElement();
        }

        public static SearchParameterInfo ToInfo(this SearchParameter searchParam)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));

            return new SearchParameterInfo(
                searchParam.Name,
                searchParam.Code,
                Enum.Parse<ValueSets.SearchParamType>(searchParam.Type?.ToString()),
                string.IsNullOrEmpty(searchParam.Url) ? null : new Uri(searchParam.Url),
                searchParam.Component?.Select(x => new SearchParameterComponentInfo(x.GetComponentDefinitionUri(), x.Expression)).ToArray(),
                searchParam.Expression,
                searchParam.Target?.Select(x => x?.ToString()).ToArray(),
                searchParam.Base?.Select(x => x?.ToString()).ToArray(),
                searchParam.Description);
        }

        public static ValueSets.SearchParamType ToValueSet(this SearchParamType searchParam)
        {
            return Enum.Parse<ValueSets.SearchParamType>(searchParam.ToString());
        }
    }
}
