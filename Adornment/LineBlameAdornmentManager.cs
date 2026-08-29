using Gittoy.GitBlame;
using Gittoy.Options;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Gittoy.Adornment
{
    internal class LineBlameAdornmentManager
    {
        private const string LayerName = "GitToolboxBlameLayer";
        private readonly IWpfTextView _textView;
        private readonly IAdornmentLayer _layer;
        private readonly GitBlameCache _cache = new();
        private readonly CommitMessageCache _messageCache = new();
        private readonly ITextDocument _document;

        private readonly List<ITrackingPoint> _dirtyLineMarkers = new();
        private HashSet<int> _dirtyLineNumbersCache = new();
        private CancellationTokenSource _debounceCts;

        public LineBlameAdornmentManager(IWpfTextView textView)
        {
            _textView = textView;
            _layer = textView.GetAdornmentLayer(LayerName);

            _textView.TextBuffer.Properties.TryGetProperty(
                typeof(ITextDocument), out _document);

            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            _textView.LayoutChanged += OnLayoutChanged;
            _textView.TextBuffer.Changed += OnBufferChanged;
            _textView.Closed += OnClosed;
            GittoySettings.SettingsChanged += OnSettingsChanged;

            if (_document != null)
            {
                _document.FileActionOccurred += OnFileActionOccurred;
                _cache.EnsurePrefetchStarted(_document.FilePath);
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            RequestUpdate();
        }
        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            var newSnapshot = e.After;

            foreach (var change in e.Changes)
            {
                int startPos = change.NewSpan.Start;
                int endPos = change.NewSpan.End;

                int startLine = newSnapshot.GetLineNumberFromPosition(startPos);
                int endLine = endPos > startPos
                    ? newSnapshot.GetLineNumberFromPosition(endPos - 1)
                    : startLine;

                for (int i = startLine; i <= endLine; i++)
                {
                    var line = newSnapshot.GetLineFromLineNumber(i);
                    var trackingPoint = newSnapshot.CreateTrackingPoint(
                        line.Start.Position, PointTrackingMode.Negative);
                    _dirtyLineMarkers.Add(trackingPoint);
                }
            }

            RebuildDirtyLineCache(newSnapshot);

            RequestUpdate();
        }

        private void RebuildDirtyLineCache(ITextSnapshot snapshot)
        {
            var result = new HashSet<int>();
            foreach (var marker in _dirtyLineMarkers)
            {
                var point = marker.GetPoint(snapshot);
                result.Add(point.GetContainingLine().LineNumber);
            }
            _dirtyLineNumbersCache = result;
        }

        private void OnFileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if (e.FileActionType == FileActionTypes.ContentSavedToDisk ||
                e.FileActionType == FileActionTypes.DocumentRenamed)
            {
                _cache.InvalidateFile(e.FilePath);
                _cache.EnsurePrefetchStarted(e.FilePath);

                _dirtyLineMarkers.Clear();
                _dirtyLineNumbersCache.Clear();

                RequestUpdate();
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            RequestUpdate();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            RequestUpdate();
        }

        private void RequestUpdate()
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = DebouncedUpdateAsync(token);
        }
        private async Task DebouncedUpdateAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(GittoySettings.DebounceDelayMs, token);
                if (token.IsCancellationRequested) return;

                await UpdateAdornmentAsync();
            }
            catch (TaskCanceledException) { }
        }

        private async Task UpdateAdornmentAsync()
        {
            _layer.RemoveAllAdornments();

            var caretPoint = _textView.Caret.Position.BufferPosition;
            var snapshotLine = caretPoint.GetContainingLine();
            int lineNumber1Based = snapshotLine.LineNumber + 1;

            string? filePath = _document?.FilePath;
            if (string.IsNullOrEmpty(filePath)) return;

            var blame = await _cache.GetOrFetchAsync(filePath!, lineNumber1Based);
            if (blame == null) return;

            if (_textView.Caret.Position.BufferPosition.GetContainingLine().LineNumber != snapshotLine.LineNumber)
                return;

            var viewLine = _textView.GetTextViewLineContainingBufferPosition(caretPoint);
            if (viewLine == null) return;

            bool isLineDirty = _dirtyLineNumbersCache.Contains(snapshotLine.LineNumber);

            RenderAdornment(viewLine, blame, isLineDirty);
        }

        private void RenderAdornment(ITextViewLine line, GitBlameInfo blame, bool isLineDirty)
        {
            string shortText = GittoySettings.ShowSummaryInline
                ? blame.ToShortText() 
                : $"{blame.Author}, {blame.AuthorTime:yyyy-MM-dd}";

            string text = isLineDirty
                ? $"  ⚠ {shortText} (此行有未保存的更改)"
                : $"  {shortText}";

            var normalBrush = new SolidColorBrush(GittoySettings.NormalTextColor);
            var dirtyBrush = new SolidColorBrush(GittoySettings.DirtyLineTextColor);

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = isLineDirty ? dirtyBrush : normalBrush,
                FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize - 1,
                Cursor = blame.IsUncommitted ? Cursors.Arrow : Cursors.Hand,
                FontFamily = _textView.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
            };

            var toolTip = new ToolTip { Content = "加载中..." };
            textBlock.ToolTip = toolTip;
            toolTip.Opened += async (s, e) => await LoadTooltipContentAsync(toolTip, blame, isLineDirty).ConfigureAwait(false);

            if (!blame.IsUncommitted)
            {
                textBlock.MouseLeftButtonDown += (s, e) => OnBlameLeftClick(textBlock, blame);
                textBlock.ContextMenu = BuildContextMenu(blame);
            }

            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double startX = Math.Max(line.TextRight, _textView.Caret.Left);

            Canvas.SetLeft(textBlock, startX);
            Canvas.SetTop(textBlock, line.TextTop);

            _layer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                new SnapshotSpan(line.Start, line.End),
                tag: null,
                adornment: textBlock,
                removedCallback: null);
        }

        private void OnBlameLeftClick(TextBlock textBlock, GitBlameInfo blame)
        {
            CopyToClipboardWithFeedback(textBlock, blame.CommitHash, "已复制 hash");
        }

        private ContextMenu BuildContextMenu(GitBlameInfo blame)
        {
            var menu = new ContextMenu();

            var copyHashItem = new MenuItem { Header = "复制 commit hash" };
            copyHashItem.Click += (s, e) =>
            {
                try { Clipboard.SetText(blame.CommitHash); } catch { /* 剪贴板偶尔会被占用，忽略 */ }
            };
            menu.Items.Add(copyHashItem);
            return menu;
        }

        /// <summary>
        /// 复制内容到剪贴板，并短暂改变文本颜色/内容作为视觉反馈，
        /// 避免用户点击后不知道有没有生效。
        /// </summary>
        private void CopyToClipboardWithFeedback(TextBlock textBlock, string content, string feedbackText)
        {
            try
            {
                Clipboard.SetText(content);
            }
            catch
            {
                return; // 剪贴板访问失败，不做反馈，避免误导
            }

            string originalText = textBlock.Text;
            var originalBrush = textBlock.Foreground;

            textBlock.Text = "  ✓ " + feedbackText;
            textBlock.Foreground = Brushes.LimeGreen;

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                textBlock.Text = originalText;
                textBlock.Foreground = originalBrush;
            };
            timer.Start();
        }

        private async Task LoadTooltipContentAsync(ToolTip toolTip, GitBlameInfo blame, bool isLineDirty)
        {
            if (blame.IsUncommitted)
            {
                toolTip.Content = "此行尚未提交";
                return;
            }

            string? filePath = _document?.FilePath;
            string? workingDir = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);

            string? fullMessage = workingDir != null
                ? await _messageCache.GetOrFetchAsync(workingDir, blame.CommitHash)
                : null;

            var sb = new StringBuilder();
            sb.AppendLine($"提交: {blame.CommitHash}");
            sb.AppendLine($"作者: {blame.Author}");
            sb.AppendLine($"时间: {blame.AuthorTime.ToString(GittoySettings.DateTimeFormat)}");
            sb.AppendLine();
            sb.Append(!string.IsNullOrWhiteSpace(fullMessage) ? fullMessage : blame.Summary);

            if (isLineDirty)
                sb.Append("\n\n⚠ 此行有未保存的更改，以上信息可能不是最新");

            // 此时代码运行在 UI 线程的事件回调里（await 之后 WPF 会自动切回 UI 线程），
            // 直接赋值是安全的
            toolTip.Content = sb.ToString();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _textView.Caret.PositionChanged -= OnCaretPositionChanged;
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.TextBuffer.Changed -= OnBufferChanged;
            _textView.Closed -= OnClosed;
            GittoySettings.SettingsChanged -= OnSettingsChanged;

            if (_document != null)
                _document.FileActionOccurred -= OnFileActionOccurred;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
    }
}