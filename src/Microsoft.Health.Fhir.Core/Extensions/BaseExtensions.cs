// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class BaseExtensions
    {
        /// <summary>
        /// Checks if two Base instances have the same values recursively by comparing their element structures.
        /// This method is compatible with Firely SDK v6.
        /// </summary>
        /// <param name="source">The first Base instance to compare.</param>
        /// <param name="sourceName">The element name for the source.</param>
        /// <param name="other">The second Base instance to compare.</param>
        /// <param name="otherName">The element name for the other.</param>
        /// <returns>Whether the two Base instances are structurally equal.</returns>
        public static bool EqualValues(this Base source, string sourceName, Base other, string otherName)
        {
            // if both are the same reference return true
            if (ReferenceEquals(source, other))
            {
                return true;
            }

            if (source == null || other == null)
            {
                return false;
            }

            if (!sourceName.Equals(otherName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!source.ToString().Equals(other.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Get child elements using Firely SDK v6 EnumerateElements()
            var sourceElements = source.EnumerateElements().ToList();
            var otherElements = other.EnumerateElements().ToList();

            if (sourceElements.Any() || otherElements.Any())
            {
                if (!sourceElements.Any() || !otherElements.Any())
                {
                    return false;
                }

                if (sourceElements.Count != otherElements.Count)
                {
                    return false;
                }

                // Compare each child element
                for (int i = 0; i < sourceElements.Count; i++)
                {
                    var sourceElement = sourceElements[i];
                    var otherElement = otherElements[i];

                    // EnumerateElements returns KeyValuePair<string, object>
                    // The value can be a Base or a List<Base>
                    if (!sourceElement.Key.Equals(otherElement.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // Handle List<Base> values
                    if (sourceElement.Value is System.Collections.IList sourceList && otherElement.Value is System.Collections.IList otherList)
                    {
                        if (sourceList.Count != otherList.Count)
                        {
                            return false;
                        }

                        for (int j = 0; j < sourceList.Count; j++)
                        {
                            if (sourceList[j] is Base sourceChild && otherList[j] is Base otherChild)
                            {
                                if (!sourceChild.EqualValues(sourceElement.Key, otherChild, otherElement.Key))
                                {
                                    return false;
                                }
                            }
                        }
                    }

                    // Handle single Base values
                    else if (sourceElement.Value is Base sourceChild && otherElement.Value is Base otherChild)
                    {
                        if (!sourceChild.EqualValues(sourceElement.Key, otherChild, otherElement.Key))
                        {
                            return false;
                        }
                    }

                    // Handle primitive values
                    else if (!Equals(sourceElement.Value, otherElement.Value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if two Base instances have the same values recursively.
        /// Convenience overload that uses the type name as the element name.
        /// </summary>
        /// <param name="source">The first Base instance to compare.</param>
        /// <param name="other">The second Base instance to compare.</param>
        /// <returns>Whether the two Base instances are structurally equal.</returns>
        public static bool EqualValues(this Base source, Base other)
        {
            if (source == null || other == null)
            {
                return source == other;
            }

            string sourceName = source.TypeName;
            string otherName = other.TypeName;

            return source.EqualValues(sourceName, other, otherName);
        }
    }
}
