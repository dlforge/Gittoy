using System.ComponentModel.Composition;
using System.Windows;                  
using Microsoft.VisualStudio.Text.Editor; 
using Microsoft.VisualStudio.Utilities;
namespace Gittoy.Adornment
{
    // Adornment/LineBlameAdornmentTextViewCreationListener.cs
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]              // 对所有代码类文件生效，也可以改成 "CSharp" 等具体类型
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class LineBlameAdornmentTextViewCreationListener : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("GitToolboxBlameLayer")]
        [Order(After = PredefinedAdornmentLayers.Text)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
        public AdornmentLayerDefinition BlameAdornmentLayer = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            // 每个视图创建一个独立的 Manager 实例，生命周期跟随 textView
            textView.Properties.GetOrCreateSingletonProperty(
                () => new LineBlameAdornmentManager(textView));
        }
    }
}
