// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface ICapabilityStatementBuilder
    {
        ICapabilityStatementBuilder AddRestInteraction(string resourceType, string interaction);

        ICapabilityStatementBuilder AddRestInteraction(string systemInteraction);

        ICapabilityStatementBuilder AddSearchParams(string resourceType, IEnumerable<SearchParamComponent> searchParameters);

        ICapabilityStatementBuilder UpdateRestResourceComponent(string resourceType, Action<ListedResourceComponent> action);

        ICapabilityStatementBuilder Update(Action<ListedCapabilityStatement> action);

        ICapabilityStatementBuilder AddDefaultResourceInteractions();

        ICapabilityStatementBuilder AddDefaultSearchParameters();

        ICapabilityStatementBuilder AddDefaultRestSearchParams();

        ITypedElement Build();
    }
}
