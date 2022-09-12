﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal sealed class DiagnosticTableItem : TableItem
    {
        public readonly DiagnosticData Data;

        private DiagnosticTableItem(
            Workspace workspace,
            DiagnosticData data,
            string? projectName,
            Guid projectGuid,
            string[] projectNames,
            Guid[] projectGuids)
            : base(workspace, projectName, projectGuid, projectNames, projectGuids)
        {
            Contract.ThrowIfNull(data);
            Data = data;
        }

        internal static DiagnosticTableItem Create(Workspace workspace, DiagnosticData data)
        {
            GetProjectNameAndGuid(workspace, data.ProjectId, out var projectName, out var projectGuid);
            return new DiagnosticTableItem(workspace, data, projectName, projectGuid, projectNames: Array.Empty<string>(), projectGuids: Array.Empty<Guid>());
        }

        public override TableItem WithAggregatedData(string[] projectNames, Guid[] projectGuids)
            => new DiagnosticTableItem(Workspace, Data, projectName: null, projectGuid: Guid.Empty, projectNames, projectGuids);

        public override DocumentId? DocumentId
            => Data.DocumentId;

        public override ProjectId? ProjectId
            => Data.ProjectId;

        public override LinePosition GetOriginalPosition()
            => Data.DataLocation?.OriginalFileSpan?.StartLinePosition ?? default;

        public override string? GetOriginalFilePath()
            => Data.DataLocation?.OriginalFileSpan?.Path;

        public override bool EqualsIgnoringLocation(TableItem other)
        {
            if (other is not DiagnosticTableItem otherDiagnosticItem)
            {
                return false;
            }

            var diagnostic = Data;
            var otherDiagnostic = otherDiagnosticItem.Data;

            // everything same except location
            return diagnostic.Id == otherDiagnostic.Id &&
                   diagnostic.ProjectId == otherDiagnostic.ProjectId &&
                   diagnostic.DocumentId == otherDiagnostic.DocumentId &&
                   diagnostic.Category == otherDiagnostic.Category &&
                   diagnostic.Severity == otherDiagnostic.Severity &&
                   diagnostic.WarningLevel == otherDiagnostic.WarningLevel &&
                   diagnostic.Message == otherDiagnostic.Message;
        }

        /// <summary>
        /// Used to group diagnostics that only differ in the project they come from.
        /// We want to avoid displaying diagnostic multuple times when it is reported from 
        /// multi-targeted projects and/or files linked to multiple projects.
        /// Note that a linked file is represented by unique <see cref="DocumentId"/> in each project it is linked to,
        /// so we don't include <see cref="DocumentId"/> in the comparison.
        /// </summary>
        internal sealed class GroupingComparer : IEqualityComparer<DiagnosticData>, IEqualityComparer<DiagnosticTableItem>
        {
            public static readonly GroupingComparer Instance = new();

            public bool Equals(DiagnosticData left, DiagnosticData right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }

                if (left is null || right is null)
                {
                    return false;
                }

                var leftLocation = left.DataLocation;
                var rightLocation = right.DataLocation;

                // location-less or project level diagnostic:
                if (leftLocation == null ||
                    rightLocation == null ||
                    leftLocation?.OriginalFileSpan?.Path == null ||
                    rightLocation?.OriginalFileSpan?.Path == null ||
                    left.DocumentId == null ||
                    right.DocumentId == null)
                {
                    return left.Equals(right);
                }

                return
                    leftLocation?.OriginalFileSpan?.Span == rightLocation?.OriginalFileSpan?.Span &&
                    left.Severity == right.Severity &&
                    left.IsSuppressed == right.IsSuppressed &&
                    left.Id == right.Id &&
                    left.Message == right.Message &&
                    leftLocation?.OriginalFileSpan?.Path == rightLocation?.OriginalFileSpan?.Path;
            }

            public int GetHashCode(DiagnosticData data)
            {
                var location = data.DataLocation;

                // location-less or project level diagnostic:
                if (location == null ||
                    location?.OriginalFileSpan?.Path == null ||
                    data.DocumentId == null)
                {
                    return data.GetHashCode();
                }

                return
                    Hash.Combine(location.OriginalFileSpan.Value.GetHashCode(),
                    Hash.Combine(data.IsSuppressed,
                    Hash.Combine(data.Id, data.Severity.GetHashCode())));
            }

            public bool Equals(DiagnosticTableItem left, DiagnosticTableItem right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }

                if (left is null || right is null)
                {
                    return false;
                }

                return Equals(left.Data, right.Data);
            }

            public int GetHashCode(DiagnosticTableItem item)
                => GetHashCode(item.Data);
        }
    }
}
