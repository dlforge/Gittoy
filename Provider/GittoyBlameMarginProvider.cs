using Gittoy.Margin;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Gittoy.Provider
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(GittoyBlameMargin.MarginName)]
    [Order(After = PredefinedMarginNames.LineNumber)]
    [MarginContainer(PredefinedMarginNames.Left)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal class GittoyBlameMarginProvider: IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost host, IWpfTextViewMargin containerMargin)
        {
            return new GittoyBlameMargin(host.TextView);
        }
    }
}
