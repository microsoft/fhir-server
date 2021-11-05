// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ModelExtensions
    {
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
                Location = issue.Location,
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
    }
}
