﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.DesignerAttribute
{
    [ExportWorkspaceService(typeof(IDesignerAttributeDiscoveryService)), Shared]
    internal sealed partial class DesignerAttributeDiscoveryService : IDesignerAttributeDiscoveryService
    {
        /// <summary>
        /// Protects mutable state in this type.
        /// </summary>
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(initialCount: 1);

        /// <summary>
        /// Keep track of the last information we reported.  We will avoid notifying the host if we recompute and these
        /// don't change.
        /// </summary>
        private readonly ConcurrentDictionary<DocumentId, (string? category, VersionStamp projectVersion)> _documentToLastReportedInformation = new();

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public DesignerAttributeDiscoveryService()
        {
        }

        public async IAsyncEnumerable<DesignerAttributeData> ProcessSolutionAsync(
            Solution solution,
            DocumentId? priorityDocumentId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using (await _gate.DisposableWaitAsync(cancellationToken).ConfigureAwait(false))
            {
                // Remove any documents that are now gone.
                foreach (var docId in _documentToLastReportedInformation.Keys)
                {
                    if (!solution.ContainsDocument(docId))
                        _documentToLastReportedInformation.TryRemove(docId, out _);
                }

                // Handle the priority doc first.
                var priorityDocument = solution.GetDocument(priorityDocumentId);
                if (priorityDocument != null)
                {
                    await foreach (var item in ProcessProjectAsync(priorityDocument.Project, priorityDocument, cancellationToken).ConfigureAwait(false))
                        yield return item;
                }

                // Process the rest of the projects in dependency order so that their data is ready when we hit the 
                // projects that depend on them.
                var dependencyGraph = solution.GetProjectDependencyGraph();
                foreach (var projectId in dependencyGraph.GetTopologicallySortedProjects(cancellationToken))
                {
                    if (projectId != priorityDocumentId?.ProjectId)
                    {
                        await foreach (var item in ProcessProjectAsync(solution.GetRequiredProject(projectId), specificDocument: null, cancellationToken).ConfigureAwait(false))
                            yield return item;
                    }
                }
            }
        }

        private async IAsyncEnumerable<DesignerAttributeData> ProcessProjectAsync(
            Project project,
            Document? specificDocument,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!project.SupportsCompilation)
                yield break;

            var compilation = await project.GetRequiredCompilationAsync(cancellationToken).ConfigureAwait(false);
            var designerCategoryType = compilation.DesignerCategoryAttributeType();
            if (designerCategoryType == null)
                yield break;

            await foreach (var item in ScanForDesignerCategoryUsageAsync(
                project, specificDocument, designerCategoryType, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            // If we scanned just a specific document in the project, now scan the rest of the files.
            if (specificDocument != null)
            {
                await foreach (var item in ScanForDesignerCategoryUsageAsync(
                    project, specificDocument: null, designerCategoryType, cancellationToken).ConfigureAwait(false))
                {
                    yield return item;
                }
            }
        }

        private async IAsyncEnumerable<DesignerAttributeData> ScanForDesignerCategoryUsageAsync(
            Project project,
            Document? specificDocument,
            INamedTypeSymbol designerCategoryType,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // We need to reanalyze the project whenever it (or any of its dependencies) have
            // changed.  We need to know about dependencies since if a downstream project adds the
            // DesignerCategory attribute to a class, that can affect us when we examine the classes
            // in this project.
            var projectVersion = await project.GetDependentSemanticVersionAsync(cancellationToken).ConfigureAwait(false);

            // Now get all the values that actually changed and notify VS about them. We don't need
            // to tell it about the ones that didn't change since that will have no effect on the
            // user experience.

            await foreach (var data in ComputeChangedDataAsync(
                project, specificDocument, projectVersion, designerCategoryType, cancellationToken).ConfigureAwait(false))
            {
                yield return data;

                // Now, keep track of what we've reported to the host so we won't report unchanged files in the future. We
                // do this after the report has gone through as we want to make sure that if it cancels for any reason we
                // don't hold onto values that may not have made it all the way to the project system.
                _documentToLastReportedInformation[data.DocumentId] = (data.Category, projectVersion);
            }
       }

        private async IAsyncEnumerable<DesignerAttributeData> ComputeChangedDataAsync(
            Project project,
            Document? specificDocument,
            VersionStamp projectVersion,
            INamedTypeSymbol designerCategoryType,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Kick off the work in parallel across the documents of interest.
            using var _1 = ArrayBuilder<Task<DesignerAttributeData?>>.GetInstance(out var tasks);
            foreach (var document in project.Documents)
            {
                // If we're only analyzing a specific document, then skip the rest.
                if (specificDocument != null && document != specificDocument)
                    continue;

                // If we don't have a path for this document, we cant proceed with it.
                // We need that path to inform the project system which file we're referring to.
                if (document.FilePath == null)
                    continue;

                // If nothing has changed at the top level between the last time we analyzed this document and now, then
                // no need to analyze again.
                if (_documentToLastReportedInformation.TryGetValue(document.Id, out var existingInfo) &&
                    existingInfo.projectVersion == projectVersion)
                {
                    continue;
                }

                tasks.Add(ComputeDesignerAttributeDataAsync(designerCategoryType, document, cancellationToken));
            }

            // Convert the tasks into one final stream we can read all the results from.
            await foreach (var dataOpt in tasks.ToImmutable().StreamAsync(cancellationToken).ConfigureAwait(false))
            {
                if (dataOpt is null)
                    continue;

                var data = dataOpt.Value;
                _documentToLastReportedInformation.TryGetValue(data.DocumentId, out var existingInfo);
                if (existingInfo.category != data.Category)
                    yield return data;
            }
        }

        private static async Task<DesignerAttributeData?> ComputeDesignerAttributeDataAsync(
            INamedTypeSymbol? designerCategoryType, Document document, CancellationToken cancellationToken)
        {
            try
            {
                Contract.ThrowIfNull(document.FilePath);

                // We either haven't computed the designer info, or our data was out of date.  We need
                // So recompute here.  Figure out what the current category is, and if that's different
                // from what we previously stored.
                var category = await DesignerAttributeHelpers.ComputeDesignerAttributeCategoryAsync(
                    designerCategoryType, document, cancellationToken).ConfigureAwait(false);

                return new DesignerAttributeData
                {
                    Category = category,
                    DocumentId = document.Id,
                    FilePath = document.FilePath,
                };
            }
            catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
            {
                return null;
            }
        }
    }
}
