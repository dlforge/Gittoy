using Gittoy.Margin;
using Gittoy.Options;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Gittoy
{
    internal sealed class ToggleBlameMarginCommand
    {
        public const int CommandId = 0x0100;
        public const int LineCommandId = 0x0101;
        public static readonly Guid CommandSet = new Guid("cb3426c6-7223-4c1e-9237-bd29c501a0f7");

        private readonly AsyncPackage package;
        private readonly IVsTextManager textManager;
        private readonly IVsEditorAdaptersFactoryService editorAdaptersFactory;
        private readonly IVsStatusbar? statusBar;

        private ToggleBlameMarginCommand(
            AsyncPackage package,
            OleMenuCommandService commandService,
            IVsTextManager textManager,
            IVsEditorAdaptersFactoryService editorAdaptersFactory,
            IVsStatusbar? statusBar)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.textManager = textManager ?? throw new ArgumentNullException(nameof(textManager));
            this.editorAdaptersFactory = editorAdaptersFactory ?? throw new ArgumentNullException(nameof(editorAdaptersFactory));
            this.statusBar = statusBar;
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            CreateCommand(commandService, CommandId);
            CreateCommand(commandService, LineCommandId);
        }

        public static ToggleBlameMarginCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            var textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            var statusBar = await package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            var editorAdaptersFactory = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            Instance = new ToggleBlameMarginCommand(package, commandService, textManager!, editorAdaptersFactory!, statusBar);
        }

        private void CreateCommand(OleMenuCommandService commandService, int commandId)
        {
            var menuCommandID = new CommandID(CommandSet, commandId);
            var command = new OleMenuCommand(Execute, menuCommandID);
            command.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(command);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is not OleMenuCommand command)
            {
                return;
            }

            command.Visible = true;
            command.Enabled = TryGetActiveTextView();
            command.Text = TryGetActiveMargin(out var margin) && margin.IsVisible ? "隐藏 Git blame 侧边栏" : "显示 Git blame 侧边栏";
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (TryGetActiveMargin(out var margin))
            {
                margin.SetVisible(!margin.IsVisible);
                ShowStatusMessage(margin.IsVisible ? "已显示 Git blame 侧边栏" : "已隐藏 Git blame 侧边栏");
                return;
            }

            if (TryGetActiveTextView())
            {
                GittoySettings.ShowBlameMargin = true;
                GittoySettings.NotifyChanged();
                ShowStatusMessage("已显示 Git blame 侧边栏");
            }
        }

        private bool TryGetActiveMargin(out GittoyBlameMargin? margin)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            margin = null;

            var textView = GetActiveTextView();
            if (textView == null)
            {
                return false;
            }

            return textView.Properties.TryGetProperty(typeof(GittoyBlameMargin), out margin);
        }

        private bool TryGetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetActiveTextView() != null;
        }

        private void ShowStatusMessage(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            statusBar?.SetText(message);
        }

        private IWpfTextView? GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsTextBuffer? buffer = null;
            if (textManager.GetActiveView(1, buffer, out IVsTextView view) != 0 || view == null)
            {
                return null;
            }

            return editorAdaptersFactory.GetWpfTextView(view);
        }
    }
}
