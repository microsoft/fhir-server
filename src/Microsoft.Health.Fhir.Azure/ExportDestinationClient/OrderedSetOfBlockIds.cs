// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    /// <summary>
    /// A modified implementation of an ordered hash set using a list and hashset. This class is specifically used
    /// for maintaining the existing block ids that are part of a blob in <see cref="CloudBlockBlobWrapper"/>. We need to
    /// maintain ordering of the block ids but also make sure that we don't have multiple copies of a block id. Hence
    /// the need for an ordered hash set.
    /// </summary>
    internal class OrderedSetOfBlockIds
    {
        private readonly List<string> _itemList;
        private readonly HashSet<string> _itemSet;

        public OrderedSetOfBlockIds()
        {
            _itemList = new List<string>();
            _itemSet = new HashSet<string>();
        }

        public OrderedSetOfBlockIds(IEnumerable<string> existingItems)
            : this()
        {
            EnsureArg.IsNotNull(existingItems, nameof(existingItems));

            foreach (string item in existingItems)
            {
                Add(item);
            }
        }

        /// <summary>
        /// Will add the given <see cref="item"/> if it is not present already.
        /// </summary>
        /// <param name="item">Item to be added to the <see cref="OrderedSetOfBlockIds"/></param>
        public void Add(string item)
        {
            EnsureArg.IsNotNullOrWhiteSpace(item, nameof(item));

            // Add item to list if the hashset does not contain it.
            if (_itemSet.Add(item))
            {
                _itemList.Add(item);
            }
        }

        /// <summary>
        /// Returns a list of all items that were added to the <see cref="OrderedSetOfBlockIds"/>
        /// </summary>
        public List<string> ToList()
        {
            return new List<string>(_itemList);
        }
    }
}
