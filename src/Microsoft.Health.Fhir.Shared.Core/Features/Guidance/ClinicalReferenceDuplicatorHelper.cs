// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Guidance
{
    public static class ClinicalReferenceDuplicatorHelper
    {
        public static bool CompareAttachment(Attachment x, Attachment y)
        {
            if (x == null || y == null)
            {
                return x == null && y == null;
            }

            return string.Equals(x.ContentType, y.ContentType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Language, y.Language, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Creation, y.Creation, StringComparison.OrdinalIgnoreCase)
                && ((x.Data != null && y.Data != null) ? x.Data.SequenceEqual(y.Data) : x.Data == null && y.Data == null)
                && ((x.Size != null && y.Size != null) ? x.Size == y.Size : x.Size == null && y.Size == null);
        }

        public static bool CompareAttachments(IReadOnlyList<Attachment> x, IReadOnlyList<Attachment> y)
        {
            if ((x?.Count ?? 0) != (y?.Count ?? 0))
            {
                return false;
            }

            return x.All(xx => y.Any(yy => CompareAttachment(xx, yy)));
        }

        public static bool CompareCoding(Coding x, Coding y)
        {
            if (x == null || y == null)
            {
                return x == null && y == null;
            }

            return string.Equals(x.System, y.System, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

            // Note: disabling comparing display since some serializer sets its value to System for some resource types but not others.
            //  && string.Equals(x.Display, y.Display, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CompareCodings(IReadOnlyList<Coding> x, IReadOnlyList<Coding> y)
        {
            if ((x?.Count ?? 0) != (y?.Count ?? 0))
            {
                return false;
            }

            return x.All(xx => y.Any(yy => CompareCoding(xx, yy)));
        }

#if !R4 && !R4B && !Stu3
        public static bool CompareCodings(IReadOnlyList<DocumentReference.ProfileComponent> x, IReadOnlyList<DocumentReference.ProfileComponent> y)
        {
            if ((x?.Count ?? 0) != (y?.Count ?? 0))
            {
                return false;
            }

            return CompareCodings(
                x.Where(xx => xx.Value?.GetType() == typeof(Coding)).Select(xx => (Coding)xx.Value).ToList(),
                y.Where(yy => yy.Value?.GetType() == typeof(Coding)).Select(yy => (Coding)yy.Value).ToList());
        }
#endif

        public static bool CompareContent(DocumentReference.ContentComponent x, DocumentReference.ContentComponent y)
        {
            if (x == null || y == null)
            {
                return x == null && y == null;
            }

#if R4 || R4B || Stu3
            return CompareCoding(x.Format, y.Format) && CompareAttachment(x.Attachment, y.Attachment);
#else
            return CompareCodings(x.Profile, y.Profile) && CompareAttachment(x.Attachment, y.Attachment);
#endif
        }

        public static bool CompareContents(IReadOnlyList<DocumentReference.ContentComponent> x, IReadOnlyList<DocumentReference.ContentComponent> y)
        {
            if ((x?.Count ?? 0) != (y?.Count ?? 0))
            {
                return false;
            }

            return x.All(xx => y.Any(yy => CompareContent(xx, yy)));
        }

        public static string ConvertToString(Coding coding)
        {
            EnsureArg.IsNotNull(coding, nameof(coding));

            return $"{coding.System}|{coding.Code}";
        }
    }
}
