// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    public class StorageChannelFactory
    {
        private IServiceProvider _serviceProvider;
        private Dictionary<SubscriptionChannelType, Type> _channelTypeMap;

        public StorageChannelFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));

            _channelTypeMap =
                typeof(ISubscriptionChannel).Assembly.GetTypes()
                    .Where(t => typeof(ISubscriptionChannel).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .Select(t => new
                    {
                        Type = t,
                        Attribute = t.GetCustomAttributes(typeof(ChannelTypeAttribute), false).FirstOrDefault() as ChannelTypeAttribute,
                    })
                    .Where(t => t.Attribute != null)
                    .ToDictionary(t => t.Attribute.ChannelType, t => t.Type);
        }

        public ISubscriptionChannel Create(SubscriptionChannelType type)
        {
            return (ISubscriptionChannel)_serviceProvider.GetService(_channelTypeMap[type]);
        }
    }
}
