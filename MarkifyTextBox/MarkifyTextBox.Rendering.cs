using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Markify.Textify
{
    public sealed partial class MarkifyTextBox
    {
        private sealed class EditorSurface : FrameworkElement
        {
            private static readonly Brush SelectionBackground = FrozenBrush(Color.FromRgb(194, 220, 247));
            private static readonly Brush BracketBackground = FrozenBrush(Color.FromRgb(255, 217, 128));
            private static readonly Brush CaretForeground = FrozenBrush(Color.FromRgb(45, 111, 176));
            private readonly MarkifyTextBox _editor;
            public EditorSurface(MarkifyTextBox editor)
            {
                _editor = editor;
                Focusable = true;
                Cursor = System.Windows.Input.Cursors.IBeam;
                SnapsToDevicePixels = true;
            }

            private static Brush FrozenBrush(Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var lines = _editor.Lines;
                double maxWidth = 0;
                if (!_editor.IsWordWrapEnabled)
                {
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        maxWidth = Math.Max(maxWidth, _editor.MeasureTextWidthForLine(lines[lineIndex].TrimEnd('\r'), lineIndex));
                    }
                }
                double width = _editor.IsWordWrapEnabled
                    ? (!double.IsInfinity(availableSize.Width) ? availableSize.Width : Math.Max(_editor.TextOrigin + 20, _editor.ActualWidth))
                    : _editor.TextOrigin + maxWidth + _editor.MeasureTextWidth("  ") + 10;
                double height = TopInset * 2 + Math.Max(_editor.LineHeight, _editor.TotalVisualHeight);
                // ScrollViewer measures its content with Infinity along scrollable axes.
                // DesiredSize must stay finite; use the text's natural extent in that case.
                if (!double.IsInfinity(availableSize.Width) && !double.IsNaN(availableSize.Width))
                    width = Math.Max(width, availableSize.Width);
                if (!double.IsInfinity(availableSize.Height) && !double.IsNaN(availableSize.Height))
                    height = Math.Max(height, availableSize.Height);
                return new Size(width, height);
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                dc.DrawRectangle(_editor.Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
                string[] lines = _editor.Lines;
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                int matchingBracket = _editor.EnableBracketHighlighting ? _editor.FindMatchingBracketPosition() : -1;
                int bracketCandidate = _editor.EnableBracketHighlighting && _editor.Caret < _editor.Text.Length && MarkifyTextBox.IsBracket(_editor.Text[_editor.Caret])
                    ? _editor.Caret : (_editor.EnableBracketHighlighting ? _editor.Caret - 1 : -1);
                int firstRow = _editor.FirstVisibleLine;
                int lastRow = Math.Max(firstRow + 1, _editor.VisibleLineEnd);

                for (int row = firstRow; row < lastRow; row++)
                {
                    int documentLine = _editor.DocumentLineAtVisibleRow(row);
                    int offset = _editor.GetLineStart(documentLine);
                    string fullLine = lines[documentLine].TrimEnd('\r');
                    int segmentStart = _editor.VisibleRowStartColumn(row);
                    int segmentEnd = segmentStart + Math.Min(_editor.VisibleRowLength(row), Math.Max(0, fullLine.Length - segmentStart));
                    int renderedStart = _editor.RenderedStartColumnForRow(row);
                    int segmentLength = Math.Max(0, segmentEnd - renderedStart);
                    string line = fullLine.Substring(renderedStart, segmentLength);
                    double rowHeight = _editor.LineHeightForVisibleRow(row);
                    double y = TopInset + _editor.VisibleRowTop(row);
                    if (_editor.HighlightCurrentLine && _editor.LineIndexAt(_editor.Caret) == documentLine)
                        dc.DrawRectangle(_editor.CurrentLineBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    if (_editor.IsWholeLineSelected(documentLine))
                        dc.DrawRectangle(SelectionBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    Brush lineBrush = _editor.Foreground;
                    int headingLevel = _editor.MarkdownHeadingLevelForLine(documentLine);
                    double fontSize = _editor.FontSizeForLine(documentLine);
                    var typeface = new Typeface(_editor.FontFamily, FontStyles.Normal,
                        headingLevel > 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

                    int visibleLength = line.Length;
                    int lineEnd = offset + fullLine.Length;
                    foreach (var span in _editor.GetSelectionSpansForLine(documentLine))
                    {
                        int from = Math.Max(span.Start, offset + renderedStart);
                        int to = Math.Min(span.Start + span.Length, offset + segmentEnd);
                        if (to <= from) continue;
                        int fromColumn = from - offset - renderedStart;
                        int toColumn = to - offset - renderedStart;
                        double x = _editor.TextOrigin + _editor.MeasureTextWidthForLine(line.Substring(0, fromColumn), documentLine);
                        double w = _editor.MeasureTextWidthForLine(line.Substring(0, toColumn), documentLine) -
                            _editor.MeasureTextWidthForLine(line.Substring(0, fromColumn), documentLine);
                        dc.DrawRectangle(SelectionBackground, null, new Rect(x, y, w, rowHeight));
                    }
                    if (matchingBracket >= offset + renderedStart && matchingBracket < offset + segmentEnd)
                        DrawBracketHighlight(dc, line, y, matchingBracket - offset - renderedStart, documentLine, rowHeight);
                    if (bracketCandidate >= offset + renderedStart && bracketCandidate < offset + segmentEnd)
                        DrawBracketHighlight(dc, line, y, bracketCandidate - offset - renderedStart, documentLine, rowHeight);
                    if (line.Length > 0)
                    {
                        var formatted = MakeText(line, typeface, lineBrush, dpi, fontSize);
                        if (_editor.EnableSyntaxHighlighting)
                            _editor.ApplySyntaxHighlighting(formatted, line, documentLine);
                        dc.DrawText(formatted, new Point(_editor.TextOrigin, y + Math.Max(0, (rowHeight - formatted.Height) / 2)));
                    }
                    if (_editor.HasFocus && _editor.CaretVisible && _editor.SelectionLength == 0 &&
                        (!_editor.IsRectangularSelection || _editor.GetSelectionSpansForLine(documentLine).Count == 0) &&
                        _editor.VisibleRowForPosition(_editor.Caret) == row &&
                        _editor.LineIndexAt(_editor.Caret) == documentLine && _editor.Caret >= offset && _editor.Caret <= lineEnd)
                    {
                        int col = Math.Max(0, Math.Min(visibleLength, _editor.Caret - offset - renderedStart));
                        double cx = _editor.TextOrigin + _editor.MeasureTextWidthForLine(line.Substring(0, col), documentLine);
                        dc.DrawRectangle(CaretForeground, null,
                            new Rect(cx, y + 1, 1.4, rowHeight - 2));
                    }
                    foreach (int secondaryCaret in _editor.AdditionalCaretPositions)
                    {
                        if (!_editor.HasFocus) break;
                        if (_editor.VisibleRowForPosition(secondaryCaret) != row) continue;
                        int localColumn = Math.Max(0, Math.Min(visibleLength, secondaryCaret - offset - renderedStart));
                        double x = _editor.TextOrigin + _editor.MeasureTextWidthForLine(line.Substring(0, localColumn), documentLine);
                        dc.DrawRectangle(CaretForeground, null, new Rect(x, y + 2, 1.2, rowHeight - 4));
                    }
                }
            }

            private void DrawBracketHighlight(DrawingContext dc, string line, double y, int column, int documentLine, double rowHeight)
            {
                if (column < 0 || column >= line.Length) return;
                double x = _editor.TextOrigin + _editor.MeasureTextWidthForLine(line.Substring(0, column), documentLine);
                double width = _editor.MeasureTextWidthForLine(line.Substring(column, 1), documentLine);
                dc.DrawRectangle(BracketBackground, null, new Rect(x, y + 1, Math.Max(2, width), rowHeight - 2));
            }

            private FormattedText MakeText(string value, Typeface typeface, Brush brush, double dpi, double fontSize)
            {
                return new FormattedText(value, CultureInfo.CurrentUICulture, _editor.FlowDirection,
                    typeface, fontSize, brush, dpi);
            }
        }
    }
}
