// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface IListedCapabilityStatement
    {
        Uri Url { get; set; }

        string Id { get; set; }

        string Version { get; set; }

        string Name { get; set; }

        bool Experimental { get; set; }

        string Publisher { get; set; }

        IList<string> Format { get; }

        void TryAddRestInteraction(string resourceType, string interaction);

        void TryAddRestInteraction(string systemInteraction);

        void BuildRestResourceComponent(string resourceType, Action<IListedResourceComponent> action);
    }
}
