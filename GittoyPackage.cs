using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Gittoy.Options; // 新增

namespace Gittoy
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(GittoyPackage.PackageGuidString)]
    // 新增：注册 Options 页面。
    // "GitToolbox" 是左侧树的一级分类名，"常规" 是二级页面名，
    // 两个 0 是资源 ID（不用本地化资源时填 0 即可）
    [ProvideOptionPage(typeof(GittoyOptionPage), "GitToolbox", "常规", 0, 0, true)]
    public sealed class GittoyPackage : AsyncPackage
    {
        public const string PackageGuidString = "de34e480-a2a7-4ec3-be13-6c548f0509b4";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }
    }
}