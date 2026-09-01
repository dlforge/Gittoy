using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gittoy.Options
{
    /// <summary>
    /// 全局配置的桥梁：Options 页面写入，MEF 组件（LineBlameAdornmentManager）读取。
    /// 用静态类是因为 MEF 组件和 Package/DialogPage 是两套独立的加载机制，
    /// 无法直接互相注入依赖，静态类是最简单的跨系统共享状态方式。
    /// </summary>
    public static class GittoySettings
    {
        public static Color TextColor { get; set; } = Colors.Gray;
        public static string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 默认不显示 Blame Margin，用户可以通过菜单命令切换显示。
        /// </summary>
        public static bool ShowBlameMargin { get; set; } = false;

        /// <summary>
        /// 设置变化时触发，供已存在的 LineBlameAdornmentManager 实例
        /// 立即刷新显示（否则要等下次光标移动才会用上新设置）。
        /// </summary>
        public static event EventHandler? SettingsChanged;
        private static GittoyOptionPage? Page =>
           (GittoyOptionPage?)GittoyPackage.Instance?.GetDialogPage(typeof(GittoyOptionPage));

        public static void RaiseSettingsChanged()
        {
            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }

        
        public static bool ShowSummaryInline
        {
            get => Page?.ShowSummaryInline ?? true;
            set
            {
                var page = Page;
                if (page == null || page.ShowSummaryInline == value) return;

                page.ShowSummaryInline = value;
                page.SaveSettingsToStorage();
                NotifyChanged();
            }
        }

        internal static void NotifyChanged() =>
            SettingsChanged?.Invoke(null, EventArgs.Empty);
    }
}