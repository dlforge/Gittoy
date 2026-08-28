using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gittoy.GitBlame
{
    // GitBlame/GitBlameInfo.cs
    public class GitBlameInfo
    {
        public string CommitHash { get; set; }
        public string Author { get; set; }
        public DateTimeOffset AuthorTime { get; set; }
        public string Summary { get; set; }

        public bool IsUncommitted =>
            CommitHash == "0000000000000000000000000000000000000000";

        public string ToShortText()
        {
            if (IsUncommitted) return "未提交的更改";
            var when = HumanizeTimeAgo(AuthorTime);
            return $"{Author}, {when} • {Summary}";
        }

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
