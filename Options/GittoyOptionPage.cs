using System.ComponentModel;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace Gittoy.Options
{
    public class GittoyOptionPage : DialogPage
    {
        [Category("外观")]
        [DisplayName("正常文本颜色")]
        [Description("blame 信息在没有未保存改动时的显示颜色")]
        public Color NormalTextColor
        {
            get => GittoySettings.NormalTextColor;
            set => GittoySettings.NormalTextColor = value;
        }

        [Category("外观")]
        [DisplayName("未保存行警告颜色")]
        [Description("当前行有未保存改动时，blame 信息的显示颜色")]
        public Color DirtyLineTextColor
        {
            get => GittoySettings.DirtyLineTextColor;
            set => GittoySettings.DirtyLineTextColor = value;
        }

        [Category("外观")]
        [DisplayName("显示提交说明")]
        [Description("是否在行内 blame 文本中显示 commit summary")]
        public bool ShowSummaryInline
        {
            get => GittoySettings.ShowSummaryInline;
            set => GittoySettings.ShowSummaryInline = value;
        }

        [Category("外观")]
        [DisplayName("时间格式")]
        [Description("Tooltip 中显示提交时间使用的格式字符串（.NET 格式，如 yyyy-MM-dd HH:mm:ss）")]
        public string DateTimeFormat
        {
            get => GittoySettings.DateTimeFormat;
            set => GittoySettings.DateTimeFormat = value;
        }

        [Category("性能")]
        [DisplayName("防抖延迟(毫秒)")]
        [Description("光标移动或编辑后，等待多久没有新操作才真正查询 git blame。值越大越省资源，但响应会更慢")]
        public int DebounceDelayMs
        {
            get => GittoySettings.DebounceDelayMs;
            set => GittoySettings.DebounceDelayMs = value;
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