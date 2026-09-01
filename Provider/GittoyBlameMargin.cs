using Gittoy.GitBlame;
using Gittoy.Options;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gittoy.Margin
{
    internal sealed class GittoyBlameMargin : Grid, IWpfTextViewMargin
    {
        /// <summary>
        /// Margin 的名称，用于在 Visual Studio 中注册和查找。
        /// </summary>
        public const string MarginName = "GittoyBlameMargin";

        /// <summary>
        /// 与文本视图关联的文档对象。用于获取文件路径和监听事件等。
        /// </summary>
        private readonly ITextDocument? _document;

        /// <summary>
        /// 与文本视图关联的视图对象。用于获取文本内容和监听布局变化等。
        /// </summary>
        private readonly IWpfTextView _textView;

        /// <summary>
        /// 存储每一行的追踪点和对应的 blame 信息。TrackingPoint 绑定的是加载 blame 数据时的快照，之后不管缓冲区如何编辑，都能正确映射到当前行的位置。
        /// </summary>
        private List<TrackedBlameLine> _trackedLines = [];

        /// <summary>
        /// 用于取消正在进行的 blame 加载操作的 CancellationTokenSource。当用户保存文件或关闭视图时，可以取消当前的加载任务。
        /// </summary>
        private CancellationTokenSource? _loadCts;

        /// <summary>
        /// 指示当前是否正在加载 blame 数据。如果为 true，则在 Margin 中显示加载占位符，而不是实际的 blame 信息。
        /// </summary>
        private bool _isLoading;

        /// <summary>
        /// Margin 的默认宽度。可以根据需要调整，但应确保足够显示 commit hash 和摘要信息。
        /// </summary>
        private const double DefaultWidth = 160;
        private const double ThumbWidth = 4;
        private readonly Thumb _resizeThumb;
        private readonly Canvas _contentCanvas;
        private readonly DispatcherTimer _redrawTimer;
        private bool _redrawPending;
        private bool _isVisible;
        private double _storedWidth = DefaultWidth;
        private Brush _committedTextBrush = Brushes.Transparent;
        private Brush _uncommittedTextBrush = Brushes.Transparent;

        /// <summary>
        /// 用于缓存 commit message 的对象，避免频繁调用 git 命令获取相同的提交信息。
        /// </summary>
        private readonly CommitMessageCache _messageCache = new();

        public GittoyBlameMargin(IWpfTextView textView)
        {
            _textView = textView;
            _textView.Properties.AddProperty(typeof(GittoyBlameMargin), this);

            // 两列：内容区自适应剩余空间，手柄区固定 2px 宽
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbWidth) });
            _contentCanvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
            _resizeThumb = new Thumb
            {
                Cursor = Cursors.SizeWE,
                Background = GetThumbIdleBrush(),
                BorderBrush = GetThumbIdleBrush(),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Foreground = GetThumbIdleBrush()
            };
            SetColumn(_contentCanvas, 0);
            Children.Add(_contentCanvas);
            _resizeThumb.MouseEnter += (s, e) => ApplyThumbBrush(GetThumbHoverBrush(), 2);
            _resizeThumb.MouseLeave += (s, e) => ApplyThumbBrush(GetThumbIdleBrush(), 1);
            _resizeThumb.DragDelta += OnResizeThumbDragDelta;
            SetColumn(_resizeThumb, 1);
            Children.Add(_resizeThumb);

            _redrawTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _redrawTimer.Tick += OnRedrawTimerTick;

            _isVisible = GittoySettings.ShowBlameMargin;
            Visibility = _isVisible ? Visibility.Visible : Visibility.Collapsed;
            Width = ClampWidth(DefaultWidth);
            _storedWidth = Width;
            RefreshTextBrushes();

            _textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out _document);
            if (_document == null)
            {
                return;
            }

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.Closed += OnClosed;
            _document.FileActionOccurred += OnFileActionOccurred;
            GittoySettings.SettingsChanged += OnSettingsChanged;
            _ = ReloadBlameAsync();
        }

        private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            ApplyThumbBrush(GetThumbDragBrush(), 2);
            _storedWidth = ClampWidth(Width + e.HorizontalChange);
            Width = _storedWidth;
        }

        private static double ClampWidth(double width) => Math.Max(100, Math.Min(400, width));

        private void ApplyThumbBrush(Brush brush, double borderThickness)
        {
            _resizeThumb.Background = brush;
            _resizeThumb.BorderBrush = brush;
            _resizeThumb.BorderThickness = new Thickness(borderThickness);
            _resizeThumb.Foreground = brush;
        }

        internal bool IsVisible => _isVisible;

        internal void SetVisible(bool visible)
        {
            if (_isVisible == visible)
            {
                return;
            }

            _isVisible = visible;
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (visible)
            {
                Width = ClampWidth(_storedWidth);
                Redraw();
                return;
            }

            _storedWidth = ClampWidth(Width);
            _contentCanvas.Children.Clear();
        }

        private void OnFileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if (e.FileActionType == FileActionTypes.ContentSavedToDisk)
            {
                _ = ReloadBlameAsync();
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            RefreshTextBrushes();
            Redraw();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            ScheduleRedraw();
        }

        /// <summary>
        /// 重新从磁盘文件跑一次 git blame --porcelain，并把结果绑定成追踪点。
        /// 只应在文件打开、保存后调用，不应在滚动/编辑时触发。
        /// </summary>
        private async Task ReloadBlameAsync()
        {
            if (_document == null) return;

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            _isLoading = true;
            Redraw();

            string filePath = _document.FilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                _isLoading = false;
                return;
            }

            // 记录发起查询时的快照——这份快照对应"磁盘上的文件内容"，
            // 因为此时刚保存或刚打开，缓冲区内容应当和磁盘一致。
            var snapshotAtLoadTime = _textView.TextBuffer.CurrentSnapshot;

            Dictionary<int, GitBlameInfo>? lineBlameMap;
            try
            {
                lineBlameMap = await GitBlameService.GetBlameForWholeFileAsync(filePath);
            }
            catch
            {
                lineBlameMap = [];
            }
            if(lineBlameMap?.Keys.Count == 0)
            {
                return;
            }

            if (token.IsCancellationRequested) return;
            var newTrackedLines = new List<TrackedBlameLine>();
            foreach (KeyValuePair<int, GitBlameInfo> kvp in lineBlameMap!)
            {
                int lineNumber1Based = kvp.Key;
                int lineIndex0Based = lineNumber1Based - 1;

                if (lineIndex0Based < 0 || lineIndex0Based >= snapshotAtLoadTime.LineCount)
                    continue;

                var line = snapshotAtLoadTime.GetLineFromLineNumber(lineIndex0Based);

                var trackingPoint = snapshotAtLoadTime.CreateTrackingPoint(
                    line.Start.Position, PointTrackingMode.Negative);

                newTrackedLines.Add(new TrackedBlameLine(trackingPoint, kvp.Value));
            }

            if (token.IsCancellationRequested) return;

            _trackedLines = newTrackedLines;
            _isLoading = false;
            Redraw();
        }
        private static Brush GetThumbIdleBrush()
        {
            var color = VSColorTheme.GetThemedColor(EnvironmentColors.ScrollBarThumbBackgroundColorKey);
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        private static Brush GetThumbHoverBrush()
        {
            var color = VSColorTheme.GetThemedColor(EnvironmentColors.ScrollBarThumbMouseOverBackgroundColorKey);
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        private static Brush GetThumbDragBrush()
        {
            var color = VSColorTheme.GetThemedColor(EnvironmentColors.ScrollBarThumbPressedBackgroundColorKey);
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        private void RefreshTextBrushes()
        {
            _committedTextBrush = CreateFrozenBrush(Color.FromArgb(190, GittoySettings.TextColor.R, GittoySettings.TextColor.G, GittoySettings.TextColor.B));
            _uncommittedTextBrush = CreateFrozenBrush(Color.FromArgb(235, Colors.DarkOrange.R, Colors.DarkOrange.G, Colors.DarkOrange.B));
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }

        private void ScheduleRedraw()
        {
            if (!_isVisible || _document == null)
            {
                return;
            }

            _redrawPending = true;
            _redrawTimer.Stop();
            _redrawTimer.Start();
        }

        private void OnRedrawTimerTick(object? sender, EventArgs e)
        {
            _redrawTimer.Stop();
            if (!_redrawPending)
            {
                return;
            }

            _redrawPending = false;
            Redraw();
        }

        private void Redraw()
        {
            _contentCanvas.Children.Clear();

            if (_document == null || !_isVisible)
            {
                return;
            }

            if (_isLoading)
            {
                RenderLoadingPlaceholder();
                return;
            }

            var currentSnapshot = _textView.TextSnapshot;
            var lineNumberToInfo = new Dictionary<int, GitBlameInfo>();
            foreach (var tracked in _trackedLines)
            {
                SnapshotPoint point;
                try
                {
                    point = tracked.TrackingPoint.GetPoint(currentSnapshot);
                }
                catch
                {
                    continue;
                }

                int currentLineNumber = currentSnapshot.GetLineNumberFromPosition(point.Position);
                if (!lineNumberToInfo.ContainsKey(currentLineNumber))
                    lineNumberToInfo[currentLineNumber] = tracked.Info;
            }

            foreach (var viewLine in _textView.TextViewLines)
            {
                if (!viewLine.IsValid) continue;

                int lineNumber = currentSnapshot.GetLineNumberFromPosition(viewLine.Start.Position);
                if (lineNumberToInfo.TryGetValue(lineNumber, out var info))
                {
                    bool isUncommitted = info.IsUncommitted;
                    var textBlock = new TextBlock
                    {
                        Text = info.ToShortText(),
                        FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize - 2,
                        FontFamily = _textView.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                        FontStyle = isUncommitted ? FontStyles.Italic : _textView.FormattedLineSource.DefaultTextProperties.Typeface.Style,
                        FontWeight = isUncommitted ? FontWeights.SemiBold : _textView.FormattedLineSource.DefaultTextProperties.Typeface.Weight,
                        Foreground = isUncommitted ? _uncommittedTextBrush : _committedTextBrush,
                        Opacity = isUncommitted ? 1.0 : 0.82
                    };

                    var toolTip = new ToolTip { Content = isUncommitted ? "未提交的更改" : "加载中..." };
                    ToolTipService.SetInitialShowDelay(textBlock, 600);
                    ToolTipService.SetBetweenShowDelay(textBlock, 200);
                    textBlock.ToolTip = toolTip;
                    if (!isUncommitted)
                    {
                        var toolTipLoadCts = new CancellationTokenSource();
                        var toolTipLoadToken = toolTipLoadCts.Token;
                        var toolTipClosed = 0;
                        toolTip.Opened += (s, e) => _ = OnToolTipOpenedAsync(toolTip, info, toolTipLoadToken);
                        toolTip.Closed += (s, e) =>
                        {
                            if (Interlocked.Exchange(ref toolTipClosed, 1) != 0)
                            {
                                return;
                            }

                            toolTipLoadCts.Cancel();
                            toolTipLoadCts.Dispose();
                        };
                    }

                    Canvas.SetLeft(textBlock, 6);
                    Canvas.SetTop(textBlock, viewLine.Top - _textView.ViewportTop);
                    _contentCanvas.Children.Add(textBlock);
                }
            }
        }

        /// <summary>
        /// 当 tooltip 打开时，异步加载完整的 commit message 并更新 tooltip 的内容。
        /// </summary>
        /// <param name="toolTip"></param>
        /// <param name="blame"></param>
        /// <returns></returns>
        private async Task OnToolTipOpenedAsync(ToolTip toolTip, GitBlameInfo blame, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(220, cancellationToken);
                if (cancellationToken.IsCancellationRequested || !toolTip.IsOpen)
                {
                    return;
                }

                await LoadTooltipContentAsync(toolTip, blame, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                toolTip.Content = "加载提交信息失败";
            }
        }

        /// <summary>
        /// 异步加载 tooltip 的内容，获取完整的 commit message 并更新 tooltip 的显示。
        /// </summary>
        /// <param name="toolTip"></param>
        /// <param name="blame"></param>
        /// <returns></returns>
        private async Task LoadTooltipContentAsync(ToolTip toolTip, GitBlameInfo blame, CancellationToken cancellationToken)
        {
            if (blame.IsUncommitted || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            string? filePath = GetFilePath();
            string? workingDir = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            if (workingDir == null)
            {
                return;
            }

            string? fullMessage = await _messageCache.GetOrFetchAsync(workingDir, blame.CommitHash);
            if (cancellationToken.IsCancellationRequested || !toolTip.IsOpen)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"提交: {blame.CommitHash}");
            sb.AppendLine($"作者: {blame.Author}");
            sb.AppendLine($"时间: {blame.AuthorTime.ToString(GittoySettings.DateTimeFormat)}");
            sb.AppendLine();
            sb.Append(!string.IsNullOrWhiteSpace(fullMessage) ? fullMessage : blame.Summary);
            toolTip.Content = sb.ToString();
        }

        private string? GetFilePath()
        {
            return _document?.FilePath;
        }

        private void RenderLoadingPlaceholder()
        {
            var textBlock = new TextBlock
            {
                Text = "加载 blame 中...",
                FontSize = _textView.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize - 1,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(textBlock, 6);
            Canvas.SetTop(textBlock, 4);
            _contentCanvas.Children.Add(textBlock);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnClosed;
            GittoySettings.SettingsChanged -= OnSettingsChanged;
            _document?.FileActionOccurred -= OnFileActionOccurred;
            _redrawTimer.Stop();
            _redrawTimer.Tick -= OnRedrawTimerTick;
            if (_loadCts != null)
            {
                _loadCts.Cancel();
                _loadCts.Dispose();
                _loadCts = null;
            }
        }

        private sealed class TrackedBlameLine(ITrackingPoint trackingPoint, GitBlameInfo info)
        {
            public ITrackingPoint TrackingPoint = trackingPoint;
            public GitBlameInfo Info = info;
        }

        // ----- IWpfTextViewMargin 接口实现 -----

        public FrameworkElement VisualElement => this;

        public double MarginSize => _isVisible ? ActualWidth : 0;

        public bool Enabled => _isVisible;

        public ITextViewMargin? GetTextViewMargin(string marginName)
        {
            return marginName == MarginName ? this : null;
        }

        public void Dispose()
        {
            OnClosed(this, EventArgs.Empty);
        }
    }
}