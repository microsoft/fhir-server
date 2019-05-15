// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public static class CapabilityStatementIntersectExtensions
    {
        public static ListedCapabilityStatement BuildRestResourceComponent(this ListedCapabilityStatement statement, ResourceType resourceType, Action<ListedResourceComponent> componentBuilder)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(componentBuilder, nameof(componentBuilder));

            var restComponent = statement.GetListedRestComponent();

            var restNode = restComponent
                .Resource
                .FirstOrDefault(x => x.Type == resourceType);

            if (restNode == null)
            {
                restNode = new ListedResourceComponent
                {
                    Type = resourceType,
                    Profile = new ResourceReference(ResourceIdentity.Core(resourceType.ToString()).AbsoluteUri),
                };
                restComponent.Resource.Add(restNode);
            }

            componentBuilder(restNode);

            return statement;
        }

        public static CapabilityStatement Intersect(this ListedCapabilityStatement system, CapabilityStatement configured, bool strictConfig)
        {
            EnsureArg.IsNotNull(system, nameof(system));
            EnsureArg.IsNotNull(configured, nameof(configured));

            var issues = new List<string>();

            var intersecting = new CapabilityStatement
            {
                // System wide values
                Id = system.Id,
                Url = system.Url?.OriginalString,
                Version = system.Version,
                Name = system.Name,
                Experimental = system.Experimental,
                Publisher = system.Publisher,
                Software = system.Software,
                FhirVersion = system.FhirVersion,
                Contact = new List<ContactDetail> { new ContactDetail { Telecom = system.Telecom?.Select(x => new ContactPoint(x.System, x.Use, x.Value)).ToList() } },

                // Intersections with user configured values
                Kind = system.Kind.IntersectEnum(configured.Kind, issues, "Kind"),
                Status = system.Status.IntersectEnum(configured.Status, issues, "Status"),
                AcceptUnknown = system.AcceptUnknown.IntersectEnum(configured.AcceptUnknown, issues, "AcceptUknown"),
                Format = system.Format?.IntersectList(configured.Format, x => x, issues, "Format"),
            };

            DateTimeOffset cDate;
            if (DateTimeOffset.TryParse(configured.Date, out cDate))
            {
                intersecting.Date = cDate.ToString("o", CultureInfo.InvariantCulture);
            }

            if (system.Rest.Any() && configured.Rest.Any())
            {
                // Only a single rest node is currently supported
                if (system.Rest.Count() > 1 || configured.Rest.Count > 1)
                {
                    throw new NotSupportedException(Core.Resources.CapabilityStatementSingleRestItem);
                }

                var systemRest = system.Rest.Single();
                var configuredRest = configured.Rest.Single();

                var rest = new CapabilityStatement.RestComponent
                {
                    Mode = systemRest.Mode.IntersectEnum(configuredRest.Mode, issues, "Rest.Mode"),
                    Documentation = systemRest.Documentation,
                    Security = systemRest.Security,
                    Interaction = systemRest.Interaction?.IntersectList(configuredRest.Interaction, x => x.Code, issues, $"Rest.Interaction"),
                    SearchParam = systemRest.SearchParam?.IntersectList(configuredRest.SearchParam, x => x.Name, issues, $"Rest.SearchParam"),
                    Operation = systemRest.Operation?.IntersectList(configuredRest.Operation, x => x.Name, issues, $"Rest.Operation"),
                };

                intersecting.Rest.Add(rest);

                var systemComponents = systemRest.Resource.Where(x => configuredRest.Resource.Select(r => r.Type).Contains(x.Type));
                foreach (var systemComponent in systemComponents)
                {
                    var configuredComponent = configuredRest.Resource.Single(x => x.Type == systemComponent.Type);

                    var interaction = new CapabilityStatement.ResourceComponent
                    {
                        // System predefined values
                        Type = systemComponent.Type,

                        // User configurable override
                        Profile = configuredComponent.Profile ?? systemComponent.Profile,

                        // Boolean intersections
                        ReadHistory = systemComponent.ReadHistory.IntersectBool(configuredComponent.ReadHistory, issues, $"Rest.Resource['{systemComponent.Type}'].ReadHistory"),
                        UpdateCreate = systemComponent.UpdateCreate.IntersectBool(configuredComponent.UpdateCreate, issues, $"Rest.Resource['{systemComponent.Type}'.UpdateCreate"),
                        ConditionalCreate = systemComponent.ConditionalCreate.IntersectBool(configuredComponent.ConditionalCreate, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalCreate"),
                        ConditionalUpdate = systemComponent.ConditionalUpdate.IntersectBool(configuredComponent.ConditionalUpdate, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalUpdate"),

                        // List intersections
                        SearchInclude = systemComponent.SearchInclude.IntersectList(configuredComponent.SearchInclude, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].SearchInclude").ToList(),
                        SearchRevInclude = systemComponent.SearchRevInclude.IntersectList(configuredComponent.SearchRevInclude, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].SearchRevInclude").ToList(),
                        Interaction = systemComponent.Interaction.IntersectList(configuredComponent.Interaction, x => x.Code, issues, $"Rest.Resource['{systemComponent.Type}'].Interaction"),
                        ReferencePolicy = systemComponent.ReferencePolicy.IntersectList(configuredComponent.ReferencePolicy, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].ReferencePolicy"),
                        SearchParam = systemComponent.SearchParam.IntersectList(configuredComponent.SearchParam, x => string.Concat(x.Name, x.Type), issues, $"Rest.Resource['{systemComponent.Type}'].SearchParam"),

                        // Listed Enumerations intersections
                        Versioning = systemComponent.Versioning.IntersectEnum(configuredComponent.Versioning, issues, $"Rest.Resource['{systemComponent.Type}'].Versioning"),
                        ConditionalRead = systemComponent.ConditionalRead.IntersectEnum(configuredComponent.ConditionalRead, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalRead"),
                        ConditionalDelete = systemComponent.ConditionalDelete.IntersectEnum(configuredComponent.ConditionalDelete, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalDelete"),
                    };

                    rest.Resource.Add(interaction);
                }

                rest.Resource = rest.Resource.OrderBy(x => x.Type.ToString()).ToList();
            }

            if (strictConfig && issues.Any())
            {
                throw new UnsupportedConfigurationException(Core.Resources.UnsupportedConfigurationMessage, issues.Select(i => new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, i)).ToArray());
            }

            return intersecting;
        }
    }
}
