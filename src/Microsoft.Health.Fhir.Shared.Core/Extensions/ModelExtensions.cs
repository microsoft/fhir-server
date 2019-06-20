// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
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

        public static CodeableConcept ToPoco(this CodingInfo model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            return new CodeableConcept(model.System, model.Code);
        }

        public static OperationOutcome.IssueComponent ToPoco(this OperationOutcomeIssue issue)
        {
            EnsureArg.IsNotNull(issue, nameof(issue));

            return new OperationOutcome.IssueComponent
            {
                Severity = Enum.Parse<OperationOutcome.IssueSeverity>(issue.Severity),
                Code = Enum.Parse<OperationOutcome.IssueType>(issue.Code),
                Diagnostics = issue.Diagnostics,
                Location = issue.Location,
            };
        }

        public static ResourceElement ToResourceElement(this Base resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource.ToTypedElement().ToResourceElement();
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
                searchParam.Type?.ToString(),
                string.IsNullOrEmpty(searchParam.Url) ? null : new Uri(searchParam.Url),
                searchParam.Component?.Select(x => new SearchParameterComponentInfo(x.GetComponentDefinitionUri(), x.Expression)).ToArray(),
                searchParam.Expression,
                searchParam.Target?.Select(x => x?.ToString()).ToArray());
        }

        public static ValueSets.SearchParamType ToValueSet(this SearchParamType searchParam)
        {
            return Enum.Parse<ValueSets.SearchParamType>(searchParam.ToString());
        }
    }
}
