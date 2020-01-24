﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Packaging;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.AddPackage
{
    internal class InstallWithPackageManagerCodeAction : CodeAction
    {
        private readonly IPackageInstallerService _installerService;
        private readonly string _packageName;

        public InstallWithPackageManagerCodeAction(
            IPackageInstallerService installerService, string packageName)
        {
            _installerService = installerService;
            _packageName = packageName;
        }

        public override string Title => FeaturesResources.Install_with_package_manager;

        protected override Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SpecializedCollections.SingletonEnumerable<CodeActionOperation>(
                new InstallWithPackageManagerCodeActionOperation(this)));
        }

        private class InstallWithPackageManagerCodeActionOperation : CodeActionOperation
        {
            private readonly InstallWithPackageManagerCodeAction _codeAction;

            public InstallWithPackageManagerCodeActionOperation(
                InstallWithPackageManagerCodeAction codeAction)
            {
                _codeAction = codeAction;
            }

            public override string Title => FeaturesResources.Install_with_package_manager;

            public override void Apply(Workspace workspace, CancellationToken cancellationToken)
            {
                _codeAction._installerService.ShowManagePackagesDialog(_codeAction._packageName);
            }
        }
    }
}
