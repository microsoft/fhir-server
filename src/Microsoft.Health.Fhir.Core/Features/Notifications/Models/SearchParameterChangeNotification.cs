// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Notifications.Models
{
    /// <summary>
    /// Notification message for SearchParameter changes.
    /// </summary>
    public class SearchParameterChangeNotification
    {
        public string InstanceId { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public SearchParameterChangeType ChangeType { get; set; }

        public IReadOnlyCollection<string> AffectedParameterUris { get; set; }

        public string TriggerSource { get; set; }
    }
}
