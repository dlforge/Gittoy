using System.ComponentModel;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace Gittoy.Options
{
    public class GittoyOptionPage : DialogPage
    {
        [Category("外观")]
        [DisplayName("文本颜色")]
        [Description("blame 信息的显示颜色")]
        public Color TextColor
        {
            get => GittoySettings.TextColor;
            set => GittoySettings.TextColor = value;
        }

        [Category("外观")]
        [DisplayName("时间格式")]
        [Description("Tooltip 中显示提交时间使用的格式字符串（.NET 格式，如 yyyy-MM-dd HH:mm:ss）")]
        public string DateTimeFormat
        {
            get => GittoySettings.DateTimeFormat;
            set => GittoySettings.DateTimeFormat = value;
        }

        /// <summary>
        /// 用户点击"确定"/"应用"关闭选项对话框时触发，
        /// 这时通知所有正在显示的 adornment 立即用新设置刷新。
        /// </summary>
        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply)
                GittoySettings.RaiseSettingsChanged();
        }
    }
}