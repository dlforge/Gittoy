using System;

namespace Gittoy.GitBlame
{
    public class GitBlameInfo
    {
        /// <summary>
        /// 表示未提交的更改的提交哈希值。
        /// </summary>
        public const string UncommittedHash = "0000000000000000000000000000000000000000";

        /// <summary>
        /// 获取或设置提交的哈希值。如果是未提交的更改，则为 <see cref="UncommittedHash"/>。
        /// </summary>
        public string? CommitHash { get; set; }

        /// <summary>
        /// 获取或设置提交的作者。如果是未提交的更改，则为 null。
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// 获取或设置提交的作者时间。如果是未提交的更改，则为默认值。
        /// </summary>
        public DateTimeOffset AuthorTime { get; set; }

        /// <summary>
        /// 获取或设置提交的摘要。如果是未提交的更改，则为 null。
        /// </summary>
        public string? Summary { get; set; }

        /// <summary>
        /// 获取一个值，指示此提交是否为未提交的更改。
        /// </summary>
        public bool IsUncommitted =>
            CommitHash == UncommittedHash;


        public string GetMarginText()
        {
            if (IsUncommitted) return string.Empty;
            return $"{CommitHash!.Substring(0, 7)} {Summary!.Substring(0, Math.Min(Summary.Length, 20))}";
        }

        /// <summary>
        /// 获取一个值，指示此提交是否为有效的提交（即不是未提交的更改）。
        /// </summary>
        /// <returns></returns>
        public string ToShortText()
        {
            if (IsUncommitted) return "未提交的更改";
            var when = HumanizeTimeAgo(AuthorTime);
            return $"{Author}, {when} • {Summary}";
        }

        /// <summary>
        /// 将时间转换为“多久以前”的格式。
        /// </summary>
        /// <param name="time">要转换的时间。</param>
        /// <returns>表示时间间隔的字符串。</returns>
        private static string HumanizeTimeAgo(DateTimeOffset time)
        {
            var span = DateTimeOffset.Now - time;
            if (span.TotalDays > 365) return $"{(int)(span.TotalDays / 365)} 年前";
            if (span.TotalDays > 30) return $"{(int)(span.TotalDays / 30)} 个月前";
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays} 天前";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours} 小时前";
            return "刚刚";
        }
    }
}
