using Gittoy.Margin;
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
        public static readonly Guid CommandSet = new Guid("cb3426c6-7223-4c1e-9237-bd29c501a0f7");

        private readonly AsyncPackage package;
        private readonly IVsTextManager textManager;
        private readonly IVsEditorAdaptersFactoryService editorAdaptersFactory;
        private readonly OleMenuCommand menuCommand;

        private ToggleBlameMarginCommand(
            AsyncPackage package,
            OleMenuCommandService commandService,
            IVsTextManager textManager,
            IVsEditorAdaptersFactoryService editorAdaptersFactory)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            this.textManager = textManager ?? throw new ArgumentNullException(nameof(textManager));
            this.editorAdaptersFactory = editorAdaptersFactory ?? throw new ArgumentNullException(nameof(editorAdaptersFactory));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            menuCommand = new OleMenuCommand(Execute, menuCommandID);
            menuCommand.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuCommand);
        }

        public static ToggleBlameMarginCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            var textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            var editorAdaptersFactory = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            Instance = new ToggleBlameMarginCommand(package, commandService, textManager!, editorAdaptersFactory!);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is not OleMenuCommand command)
            {
                return;
            }

            command.Visible = TryGetActiveMargin(out var margin);
            command.Enabled = command.Visible;
            command.Text = margin?.IsVisible == true ? "隐藏 Git blame" : "显示 Git blame";
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetActiveMargin(out var margin))
            {
                return;
            }

            margin.SetVisible(!margin.IsVisible);
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
