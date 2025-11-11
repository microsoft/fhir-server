// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ElementValueExtensions
    {
        /// <summary>
        /// Checks if two ElementValue instances have the same values recursively.
        /// </summary>
        /// <param name="source">The first ElementValue instance to compare.</param>
        /// <param name="other">The second ElementValue instance to compare.</param>
        /// <returns>Whether the two ElementValues are equal.</returns>
        public static bool EqualValues(this ElementValue source, ElementValue other)
        {
            // if both are the same reference return true
            if (source.Equals(other))
            {
                return true;
            }

            if (!source.ElementName.Equals(other.ElementName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!source.Value.ToString().Equals(other.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((source.Value.NamedChildren != null && source.Value.NamedChildren.Any()) || (other.Value.NamedChildren != null && other.Value.NamedChildren.Any()))
            {
                if (source.Value.NamedChildren == null || !source.Value.NamedChildren.Any())
                {
                    return false;
                }

                if (other.Value.NamedChildren == null || !other.Value.NamedChildren.Any())
                {
                    return false;
                }

                IEnumerator<ElementValue> sourceChildren = source.Value.NamedChildren.GetEnumerator();
                IEnumerator<ElementValue> otherChildren = other.Value.NamedChildren.GetEnumerator();
                while (true)
                {
                    var sourceMoved = sourceChildren.MoveNext();
                    var otherMoved = otherChildren.MoveNext();

                    // if one has more children than the other return false
                    if (sourceMoved != otherMoved)
                    {
                        return false;
                    }

                    // if both are done, break
                    if (!sourceMoved)
                    {
                        break;
                    }

                    // if current children are not equal return false
                    if (!sourceChildren.Current.EqualValues(otherChildren.Current))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
