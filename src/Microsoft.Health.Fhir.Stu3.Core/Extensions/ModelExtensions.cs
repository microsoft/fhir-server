// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ModelExtensions
    {
        static ModelExtensions()
        {
            SetModelFactory();
        }

        public static void SetModelFactory()
        {
            ModelFactory.SetModelFactory(new Stu3ModelFactory());
        }

        public static CodeableConcept ToPoco(this CodeableConceptInfo model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            return new CodeableConcept(model.System, model.Value);
        }

        public static OperationOutcome.IssueComponent ToPoco(this OperationOutcomeIssue issue)
        {
            EnsureArg.IsNotNull(issue, nameof(issue));

            return new OperationOutcome.IssueComponent
            {
                Severity = Enum.Parse<OperationOutcome.IssueSeverity>(issue.Severity),
                Code = Enum.Parse<OperationOutcome.IssueType>(issue.Code),
                Diagnostics = issue.Diagnostics,
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

            return resource.Instance.ToPoco<T>();
        }

        public static Resource ToPoco(this ResourceElement resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource.ToPoco<Resource>();
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
