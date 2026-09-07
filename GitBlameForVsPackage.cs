using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GitBlameForVs
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class GitBlameForVsPackage : AsyncPackage
    {
        public const string PackageGuidString = "de34e480-a2a7-4ec3-be13-6c548f0509b4";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }

        public static GitBlameForVsPackage? Instance { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Instance = null;
            }
            base.Dispose(disposing);
        }
    }
}