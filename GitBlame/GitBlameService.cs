using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;

namespace Gittoy.GitBlame;
public class GitBlameService
{
    /// <summary>
    /// 获取指定文件指定行的 blame 信息。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="lineNumber1Based">行号（从 1 开始）</param>
    /// <returns>返回对应行的 GitBlameInfo，如果文件不存在或发生错误则返回 null</returns>
    public static async Task<GitBlameInfo?> GetBlameAsync(string filePath, int lineNumber1Based)
    {
        if (!File.Exists(filePath)) return null;

        var workingDir = Path.GetDirectoryName(filePath);
        var args = $"blame -L {lineNumber1Based},{lineNumber1Based} --porcelain -- \"{Path.GetFileName(filePath)}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(psi);
            string output = await process.StandardOutput.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit(3000));
            return ParsePorcelain(output);
        }
        catch
        {
            // git 未安装 / 不是 git 仓库 / 文件未跟踪等，静默失败
            return null;
        }
    }

    /// <summary>
    /// 解析 git blame --porcelain 输出，提取出 commit hash、author、author-time、summary 等信息。
    /// </summary>
    /// <param name="output">git blame --porcelain 输出内容</param>
    /// <returns>返回解析后的 GitBlameInfo，如果解析失败则返回 null</returns>
    private static GitBlameInfo? ParsePorcelain(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var lines = output.Split('\n');
        var info = new GitBlameInfo
        {
            CommitHash = lines[0].Split(' ')[0]
        };

        long authorTimeUnix = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("author "))
                info.Author = line.Substring("author ".Length).Trim();
            else if (line.StartsWith("author-time "))
                long.TryParse(line.Substring("author-time ".Length).Trim(), out authorTimeUnix);
            else if (line.StartsWith("summary "))
                info.Summary = line.Substring("summary ".Length).Trim();
        }
        info.AuthorTime = DateTimeOffset.FromUnixTimeSeconds(authorTimeUnix);
        return info;
    }

    /// <summary>
    /// 获取指定 commit 的完整提交信息（commit message）。
    /// </summary>
    /// <param name="workingDir"></param>
    /// <param name="commitHash"></param>
    /// <returns></returns>
    public static async Task<string?> GetFullCommitMessageAsync(string workingDir, string commitHash)
    {
        if (string.IsNullOrEmpty(commitHash) || commitHash.StartsWith("0000000"))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"log -1 --format=%B {commitHash}",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(psi);
            string output = await process.StandardOutput.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit(3000));
            return output?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 一次性获取整个文件所有行的 blame 信息。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns></returns>
    public static async Task<Dictionary<int, GitBlameInfo>?> GetBlameForWholeFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var workingDir = Path.GetDirectoryName(filePath);
        var args = $"blame --porcelain -- \"{Path.GetFileName(filePath)}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(psi);
            string output = await process.StandardOutput.ReadToEndAsync();
            // 全文件 blame 比单行慢很多，大文件给更宽松的超时时间
            await Task.Run(() => process.WaitForExit(15000));
            return ParsePorcelainWholeFile(output);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<int, GitBlameInfo> ParsePorcelainWholeFile(string output)
    {
        var result = new Dictionary<int, GitBlameInfo>();
        if (string.IsNullOrWhiteSpace(output)) return result;

        // 同一个 commit 的 metadata 只在第一次出现时输出，
        // 用这个字典按 hash 复用已经解析过的 GitBlameInfo
        var commitCache = new Dictionary<string, GitBlameInfo>();

        var lines = output.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.Length == 0) { i++; continue; }

            var parts = line.Split(' ');
            // header 行格式: <40位hash> <原始行号> <最终行号> [<本组行数，仅首次出现时有>]
            bool isHeaderLine = parts.Length >= 3
                && parts[0].Length == 40
                && int.TryParse(parts[2], out int finalLine);

            if (!isHeaderLine) { i++; continue; }

            string hash = parts[0];
            finalLine = int.Parse(parts[2]);

            if (!commitCache.TryGetValue(hash, out var info))
            {
                info = new GitBlameInfo { CommitHash = hash };
                commitCache[hash] = info;
            }

            i++;

            // 逐行读取 metadata，直到遇到以 \t 开头的内容行为止
            // （如果是已见过的 commit，这里会立即遇到 \t，不会进入循环体，等价于跳过）
            while (i < lines.Length && !lines[i].StartsWith("\t"))
            {
                var metaLine = lines[i];
                if (metaLine.StartsWith("author "))
                    info.Author = metaLine.Substring("author ".Length).Trim();
                else if (metaLine.StartsWith("author-time "))
                {
                    if (long.TryParse(metaLine.Substring("author-time ".Length).Trim(), out long unix))
                        info.AuthorTime = DateTimeOffset.FromUnixTimeSeconds(unix);
                }
                else if (metaLine.StartsWith("summary "))
                    info.Summary = metaLine.Substring("summary ".Length).Trim();
                i++;
            }

            if (i < lines.Length) i++; // 跳过内容行（\t 开头那行）

            result[finalLine] = info;
        }

        return result;
    }
}
