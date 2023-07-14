// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Models;

public class ListFactory : IListFactory
{
    public Resource CreateListFromReferences(string listId, params ResourceReference[] resourceReferences)
    {
        EnsureArg.IsNotNull(listId);
        EnsureArg.HasItems(resourceReferences, nameof(resourceReferences));

        var list = new List
        {
            Id = listId,
        };

        list.Mode = ListMode.Changes;
        list.Status = List.ListStatus.Current;

        foreach (var resource in resourceReferences)
        {
            list.Entry.Add(new List.EntryComponent
            {
                Item = resource,
            });
        }

        return list;
    }
}
