// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        public static OperationOutcome.IssueComponent ToPoco(this OperationOutcomeIssue issue)
        {
            EnsureArg.IsNotNull(issue, nameof(issue));

            CodeableConcept details = null;
            var coding = new List<Coding>();
            if (issue.DetailsCodes != null)
            {
                coding = issue.DetailsCodes.Coding.Select(x => new Coding(x.System, x.Code, x.Display)).ToList();
            }

            if (coding.Count != 0 || issue.DetailsText != null)
            {
                details = new CodeableConcept()
                {
                    Coding = coding,
                    Text = issue.DetailsText,
                };
            }

            return new OperationOutcome.IssueComponent
            {
                Severity = Enum.Parse<OperationOutcome.IssueSeverity>(issue.Severity),
                Code = Enum.Parse<OperationOutcome.IssueType>(issue.Code),
                Details = details,
                Diagnostics = issue.Diagnostics,
#pragma warning disable CS0618 // Type or member is obsolete
                Location = issue.Location,
#pragma warning restore CS0618 // Type or member is obsolete
                Expression = issue.Expression,
            };
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
