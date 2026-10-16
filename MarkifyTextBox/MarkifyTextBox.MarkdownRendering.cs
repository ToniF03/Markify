using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Markify.Textify
{
    public sealed partial class MarkifyTextBox
    {
        internal struct MarkdownTableCell
        {
            public int Start;
            public int Length;

            public MarkdownTableCell(int start, int length)
            {
                Start = start;
                Length = length;
            }
        }

        internal sealed class MarkdownTableLayout
        {
            public MarkdownTableCell[] Cells;
            public double[] PixelColumnWidths;
            public int[] Alignments;
            public bool IsSeparator;
            public bool IsHeader;
            public bool IsSourceVisible;
        }

        private sealed class ParsedMarkdownTableRow
        {
            public MarkdownTableCell[] Cells;
            public bool IsSeparator;
        }

        internal struct MarkdownTaskMarker
        {
            public int Start;
            public int End;
            public bool IsChecked;

            public MarkdownTaskMarker(int start, int end, bool isChecked)
            {
                Start = start;
                End = end;
                IsChecked = isChecked;
            }
        }

        internal struct MarkdownLinkSpan
        {
            public int Start;
            public int LabelStart;
            public int LabelLength;
            public int TargetStart;
            public int TargetLength;
            public int End;

            public MarkdownLinkSpan(int start, int labelStart, int labelLength, int targetStart, int targetLength, int end)
            {
                Start = start;
                LabelStart = labelStart;
                LabelLength = labelLength;
                TargetStart = targetStart;
                TargetLength = targetLength;
                End = end;
            }
        }

        internal struct MarkdownMediaBlock
        {
            public int Start;
            public int End;
            public string Source;
            public string AltText;
            public bool IsVideo;

            public MarkdownMediaBlock(int start, int end, string source, string altText, bool isVideo)
            {
                Start = start;
                End = end;
                Source = source;
                AltText = altText;
                IsVideo = isVideo;
            }
        }

        private const double MarkdownImageMaxWidth = 480;
        private const double MarkdownImageMaxHeight = 260;
        private const double MarkdownVideoWidth = 480;
        private const double MarkdownVideoHeight = 290;
        private readonly Dictionary<string, ImageSource> _markdownImageCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, MarkdownVideoPlayer> _markdownVideoPlayers =
            new Dictionary<int, MarkdownVideoPlayer>();
        private Canvas _markdownMediaCanvas;

        internal void ApplyMarkdownTaskMarkerStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (formatted == null || sourceColumns == null || sourceColumns.Length == 0) return;
            MarkdownTaskMarker marker;
            if (!TryGetMarkdownTaskMarkerForLine(line, out marker) || MarkdownTaskMarkerIsVisible(line, marker)) return;
            for (int index = 0; index < sourceColumns.Length; index++)
            {
                if (sourceColumns[index] != marker.Start) continue;
                formatted.SetFontFamily(new FontFamily("Segoe UI Symbol"), index, 1);
                formatted.SetFontWeight(FontWeights.Normal, index, 1);
                formatted.SetForegroundBrush(marker.IsChecked ? MarkdownTaskForeground : MarkdownTaskUncheckedForeground, index, 1);
            }
        }

        internal void RebuildMarkdownTableCache()
        {
            _markdownTableLayouts.Clear();
            var consumedLines = new HashSet<int>();
            for (int separatorLine = 1; separatorLine < _lineCache.Length; separatorLine++)
            {
                if (consumedLines.Contains(separatorLine)) continue;
                ParsedMarkdownTableRow separator = ParseMarkdownTableRow(separatorLine);
                if (separator == null || !separator.IsSeparator) continue;
                ParsedMarkdownTableRow header = ParseMarkdownTableRow(separatorLine - 1);
                if (header == null || header.IsSeparator || header.Cells.Length != separator.Cells.Length) continue;

                int startLine = separatorLine - 1;
                int endLine = separatorLine + 1;
                while (endLine < _lineCache.Length)
                {
                    ParsedMarkdownTableRow next = ParseMarkdownTableRow(endLine);
                    if (next == null || next.IsSeparator || next.Cells.Length != header.Cells.Length) break;
                    endLine++;
                }

                double[] pixelWidths = new double[header.Cells.Length];
                int[] alignments = new int[header.Cells.Length];
                for (int column = 0; column < pixelWidths.Length; column++)
                {
                    string separatorValue = GetTableCellText(separatorLine, separator.Cells[column]);
                    bool left = separatorValue.StartsWith(":", StringComparison.Ordinal);
                    bool right = separatorValue.EndsWith(":", StringComparison.Ordinal);
                    alignments[column] = left && right ? 1 : (right ? 2 : 0);
                }
                for (int rowLine = startLine; rowLine < endLine; rowLine++)
                {
                    ParsedMarkdownTableRow row = rowLine == separatorLine ? separator : ParseMarkdownTableRow(rowLine);
                    if (row == null || row.IsSeparator || row.Cells.Length != pixelWidths.Length) continue;
                    for (int column = 0; column < pixelWidths.Length; column++)
                    {
                        MarkdownTableCell cell = row.Cells[column];
                        RenderedTextSegment renderedCell = CreateRenderedTextSegment(rowLine,
                            cell.Start, cell.Start + cell.Length);
                        double cellWidth = MarkdownTableTextWidth(renderedCell, rowLine,
                            renderedCell.Text.Length, rowLine == startLine);
                        pixelWidths[column] = Math.Max(pixelWidths[column], cellWidth);
                    }
                }
                for (int column = 0; column < pixelWidths.Length; column++)
                    pixelWidths[column] = Math.Max(48, pixelWidths[column] + 20);

                for (int rowLine = startLine; rowLine < endLine; rowLine++)
                {
                    ParsedMarkdownTableRow row = rowLine == separatorLine ? separator : ParseMarkdownTableRow(rowLine);
                    if (row == null || row.Cells.Length != pixelWidths.Length) continue;
                    _markdownTableLayouts[rowLine] = new MarkdownTableLayout
                    {
                        Cells = row.Cells,
                        PixelColumnWidths = pixelWidths,
                        Alignments = alignments,
                        IsSeparator = row.IsSeparator,
                        IsHeader = rowLine == startLine,
                        IsSourceVisible = MarkdownTableSyntaxIsVisible(rowLine)
                    };
                    consumedLines.Add(rowLine);
                }
                consumedLines.Add(separatorLine);
            }
        }

        private ParsedMarkdownTableRow ParseMarkdownTableRow(int line)
        {
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line)) return null;
            string value = _lineCache[line].TrimEnd('\r');
            var separators = new List<int>();
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] != '|') continue;
                int backslashes = 0;
                for (int previous = index - 1; previous >= 0 && value[previous] == '\\'; previous--) backslashes++;
                if (backslashes % 2 == 0) separators.Add(index);
            }
            if (separators.Count == 0) return null;
            var bounds = new List<int> { -1 };
            bounds.AddRange(separators);
            bounds.Add(value.Length);
            var cells = new List<MarkdownTableCell>();
            for (int index = 0; index < bounds.Count - 1; index++)
            {
                int rawStart = bounds[index] + 1;
                int rawEnd = bounds[index + 1];
                if (index == 0 && string.IsNullOrWhiteSpace(value.Substring(0, bounds[1]))) continue;
                if (index == bounds.Count - 2 &&
                    string.IsNullOrWhiteSpace(value.Substring(rawStart, rawEnd - rawStart))) continue;
                int start = rawStart;
                int end = rawEnd;
                while (start < end && char.IsWhiteSpace(value[start])) start++;
                while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
                cells.Add(new MarkdownTableCell(start, end - start));
            }
            if (cells.Count < 2) return null;
            bool isSeparator = true;
            foreach (MarkdownTableCell cell in cells)
            {
                string cellText = value.Substring(cell.Start, cell.Length);
                if (!Regex.IsMatch(cellText, @"^:?-{3,}:?$"))
                {
                    isSeparator = false;
                    break;
                }
            }
            return new ParsedMarkdownTableRow { Cells = cells.ToArray(), IsSeparator = isSeparator };
        }

        private string GetTableCellText(int line, MarkdownTableCell cell)
        {
            string value = _lineCache[line].TrimEnd('\r');
            return value.Substring(cell.Start, cell.Length);
        }

        internal bool TryGetMarkdownTableLayout(int line, out MarkdownTableLayout layout)
        {
            return _markdownTableLayouts.TryGetValue(line, out layout);
        }

        internal double MarkdownTableWidth(MarkdownTableLayout layout)
        {
            double width = 0;
            if (layout == null || layout.PixelColumnWidths == null) return width;
            foreach (double columnWidth in layout.PixelColumnWidths) width += columnWidth;
            return width;
        }

        internal RenderedTextSegment MarkdownTableCellSegment(int line, MarkdownTableLayout layout, int column)
        {
            MarkdownTableCell cell = layout.Cells[column];
            return CreateRenderedTextSegment(line, cell.Start, cell.Start + cell.Length);
        }

        internal double MarkdownTableCellTextOrigin(int line, MarkdownTableLayout layout,
            int column, RenderedTextSegment segment)
        {
            double origin = TextOriginForLine(line);
            for (int previous = 0; previous < column; previous++) origin += layout.PixelColumnWidths[previous];
            double contentWidth = Math.Max(0, layout.PixelColumnWidths[column] - 16);
            double textWidth = MarkdownTableTextWidth(segment, line, segment.Text.Length, layout.IsHeader);
            if (layout.Alignments[column] == 1)
                origin += 8 + Math.Max(0, (contentWidth - textWidth) / 2);
            else if (layout.Alignments[column] == 2)
                origin += 8 + Math.Max(0, contentWidth - textWidth);
            else
                origin += 8;
            return origin;
        }

        internal double MarkdownTableTextWidth(RenderedTextSegment segment, int line, int displayLength,
            bool isHeader)
        {
            displayLength = Math.Max(0, Math.Min(segment.Text.Length, displayLength));
            if (displayLength == 0) return 0;
            string value = segment.Text.Substring(0, displayLength);
            var typeface = new Typeface(FontFamily, FontStyles.Normal,
                isHeader ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            if (EnableSyntaxHighlighting) ApplySyntaxHighlighting(formatted, value, line);
            var sourceColumns = new int[displayLength];
            Array.Copy(segment.SourceColumns, sourceColumns, displayLength);
            ApplyMarkdownBold(formatted, line, sourceColumns);
            ApplyMarkdownItalic(formatted, line, sourceColumns);
            ApplyMarkdownStrikethrough(formatted, line, sourceColumns);
            ApplyMarkdownScriptStyle(formatted, line, sourceColumns);
            ApplyMarkdownInlineCode(formatted, line, sourceColumns);
            ApplyMarkdownTaskMarkerStyle(formatted, line, sourceColumns);
            ApplyMarkdownLinkStyle(formatted, line, sourceColumns);
            ApplyMarkdownListMarkerStyle(formatted, line, sourceColumns);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        internal int PositionFromMarkdownTablePoint(int line, double pointX, MarkdownTableLayout layout)
        {
            int lineStart = GetLineStart(line);
            if (layout.IsSeparator)
            {
                int headerLine = line - 1;
                MarkdownTableLayout headerLayout;
                if (!TryGetMarkdownTableLayout(headerLine, out headerLayout) || headerLayout.IsSeparator)
                    return lineStart;
                double x = TextOriginForLine(line);
                int column = 0;
                while (column + 1 < headerLayout.PixelColumnWidths.Length &&
                    pointX >= x + headerLayout.PixelColumnWidths[column])
                {
                    x += headerLayout.PixelColumnWidths[column];
                    column++;
                }
                MarkdownTableCell headerCell = headerLayout.Cells[column];
                return GetLineStart(headerLine) + headerCell.Start + headerCell.Length;
            }
            for (int column = 0; column < layout.Cells.Length; column++)
            {
                RenderedTextSegment segment = MarkdownTableCellSegment(line, layout, column);
                double textOrigin = MarkdownTableCellTextOrigin(line, layout, column, segment);
                double cellRight = TextOriginForLine(line);
                for (int previous = 0; previous <= column; previous++)
                    cellRight += layout.PixelColumnWidths[previous];
                if (pointX > cellRight && column + 1 < layout.Cells.Length) continue;
                if (segment.Text.Length == 0) return lineStart + layout.Cells[column].Start;

                double targetX = Math.Max(0, pointX - textOrigin);
                int low = 0;
                int high = segment.Text.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (MarkdownTableTextWidth(segment, line, middle, layout.IsHeader) <= targetX) low = middle;
                    else high = middle - 1;
                }
                int displayColumn = low;
                if (displayColumn < segment.Text.Length)
                {
                    double left = MarkdownTableTextWidth(segment, line, displayColumn, layout.IsHeader);
                    double right = MarkdownTableTextWidth(segment, line, displayColumn + 1, layout.IsHeader);
                    if (targetX - left > (right - left) / 2) displayColumn++;
                }
                return lineStart + segment.SourceBoundaries[displayColumn];
            }
            MarkdownTableCell lastCell = layout.Cells[layout.Cells.Length - 1];
            return lineStart + lastCell.Start + lastCell.Length;
        }

        internal bool RefreshMarkdownTableSourceVisibility()
        {
            bool changed = false;
            foreach (KeyValuePair<int, MarkdownTableLayout> entry in _markdownTableLayouts)
            {
                bool sourceVisible = MarkdownTableSyntaxIsVisible(entry.Key);
                if (entry.Value.IsSourceVisible == sourceVisible) continue;
                entry.Value.IsSourceVisible = sourceVisible;
                changed = true;
            }
            if (changed) RebuildVisibleLineMap();
            return changed;
        }

        internal bool MarkdownTableSyntaxIsVisible(int line)
        {
            // Table rows stay rendered while editing. Caret and selection positions
            // are mapped to cell contents so the Markdown delimiters remain intact.
            return false;
        }

        internal int NormalizeMarkdownTableCaretPosition(int position, int previousPosition)
        {
            int bounded = Math.Max(0, Math.Min(Text.Length, position));
            int line = LineIndexAt(bounded);
            MarkdownTableLayout layout;
            if (!TryGetMarkdownTableLayout(line, out layout)) return bounded;

            int direction = bounded.CompareTo(previousPosition);
            if (layout.IsSeparator)
            {
                int preferredLine = direction >= 0 ? line + 1 : line - 1;
                MarkdownTableLayout preferredLayout;
                if (!TryGetMarkdownTableLayout(preferredLine, out preferredLayout) || preferredLayout.IsSeparator)
                {
                    preferredLine = direction >= 0 ? line - 1 : line + 1;
                    if (!TryGetMarkdownTableLayout(preferredLine, out preferredLayout) || preferredLayout.IsSeparator)
                        return bounded;
                }
                MarkdownTableCell preferredCell = direction >= 0
                    ? preferredLayout.Cells[0]
                    : preferredLayout.Cells[preferredLayout.Cells.Length - 1];
                return GetLineStart(preferredLine) + preferredCell.Start +
                    (direction >= 0 ? 0 : preferredCell.Length);
            }

            int lineStart = GetLineStart(line);
            int column = bounded - lineStart;
            foreach (MarkdownTableCell cell in layout.Cells)
                if (column >= cell.Start && column <= cell.Start + cell.Length)
                    return bounded;

            if (direction >= 0)
            {
                foreach (MarkdownTableCell cell in layout.Cells)
                    if (cell.Start >= column) return lineStart + cell.Start;
                MarkdownTableCell last = layout.Cells[layout.Cells.Length - 1];
                return lineStart + last.Start + last.Length;
            }
            for (int index = layout.Cells.Length - 1; index >= 0; index--)
            {
                MarkdownTableCell cell = layout.Cells[index];
                if (cell.Start + cell.Length <= column) return lineStart + cell.Start + cell.Length;
            }
            return lineStart + layout.Cells[0].Start;
        }

        internal bool TryGetMarkdownTableHorizontalDestination(int position, int direction, out int destination)
        {
            destination = position;
            int line = LineIndexAt(position);
            MarkdownTableLayout layout;
            if (!TryGetMarkdownTableLayout(line, out layout) || layout.IsSeparator) return false;
            int lineStart = GetLineStart(line);
            int column = position - lineStart;
            int cellIndex = -1;
            for (int index = 0; index < layout.Cells.Length; index++)
            {
                MarkdownTableCell cell = layout.Cells[index];
                if (column < cell.Start || column > cell.Start + cell.Length) continue;
                cellIndex = index;
                break;
            }
            if (cellIndex < 0) return false;

            MarkdownTableCell currentCell = layout.Cells[cellIndex];
            if (direction > 0 && column >= currentCell.Start + currentCell.Length)
            {
                if (cellIndex + 1 < layout.Cells.Length)
                {
                    destination = lineStart + layout.Cells[cellIndex + 1].Start;
                    return true;
                }
                for (int nextLine = line + 1; nextLine < LineCount; nextLine++)
                {
                    MarkdownTableLayout nextLayout;
                    if (TryGetMarkdownTableLayout(nextLine, out nextLayout))
                    {
                        if (nextLayout.IsSeparator) continue;
                        destination = GetLineStart(nextLine) + nextLayout.Cells[0].Start;
                        return true;
                    }
                    destination = GetLineStart(nextLine);
                    return true;
                }
                destination = GetLineEndAtLine(line);
                return true;
            }
            if (direction < 0 && column <= currentCell.Start)
            {
                if (cellIndex > 0)
                {
                    MarkdownTableCell previousCell = layout.Cells[cellIndex - 1];
                    destination = lineStart + previousCell.Start + previousCell.Length;
                    return true;
                }
                for (int previousLine = line - 1; previousLine >= 0; previousLine--)
                {
                    MarkdownTableLayout previousLayout;
                    if (TryGetMarkdownTableLayout(previousLine, out previousLayout))
                    {
                        if (previousLayout.IsSeparator) continue;
                        MarkdownTableCell previousCell = previousLayout.Cells[previousLayout.Cells.Length - 1];
                        destination = GetLineStart(previousLine) + previousCell.Start + previousCell.Length;
                        return true;
                    }
                    destination = GetLineEndAtLine(previousLine);
                    return true;
                }
                destination = position;
                return true;
            }
            return false;
        }

        internal int ClampMarkdownTablePositionToCell(int anchorPosition, int targetPosition)
        {
            int anchorLine = LineIndexAt(Math.Max(0, Math.Min(Text.Length, anchorPosition)));
            int targetLine = LineIndexAt(Math.Max(0, Math.Min(Text.Length, targetPosition)));
            MarkdownTableLayout layout;
            if (!TryGetMarkdownTableLayout(anchorLine, out layout) || layout.IsSeparator)
            {
                MarkdownTableLayout targetLayout;
                if (!TryGetMarkdownTableLayout(targetLine, out targetLayout) || targetLayout.IsSeparator)
                    return targetPosition;
                int firstTableLine = targetLine;
                int lastTableLine = targetLine;
                MarkdownTableLayout adjacentLayout;
                while (firstTableLine > 0 &&
                    TryGetMarkdownTableLayout(firstTableLine - 1, out adjacentLayout)) firstTableLine--;
                while (lastTableLine + 1 < LineCount &&
                    TryGetMarkdownTableLayout(lastTableLine + 1, out adjacentLayout)) lastTableLine++;
                int tableStart = GetLineStart(firstTableLine);
                int tableEnd = lastTableLine + 1 < LineCount
                    ? GetLineStart(lastTableLine + 1) : GetLineEndAtLine(lastTableLine);
                if (anchorPosition < tableStart) return tableStart;
                if (anchorPosition > tableEnd) return tableEnd;
                return targetPosition;
            }
            int lineStart = GetLineStart(anchorLine);
            int anchorColumn = anchorPosition - lineStart;
            foreach (MarkdownTableCell cell in layout.Cells)
            {
                int cellStart = cell.Start;
                int cellEnd = cell.Start + cell.Length;
                if (anchorColumn < cellStart || anchorColumn > cellEnd) continue;
                if (anchorLine != targetLine)
                    return lineStart + (targetPosition < anchorPosition ? cellStart : cellEnd);
                int targetColumn = targetPosition - lineStart;
                return lineStart + Math.Max(cellStart, Math.Min(cellEnd, targetColumn));
            }
            return targetPosition;
        }

        internal bool IsMarkdownTablePipe(int position)
        {
            if (position < 0 || position >= Text.Length || Text[position] != '|') return false;
            MarkdownTableLayout layout;
            int line = LineIndexAt(position);
            if (!TryGetMarkdownTableLayout(line, out layout)) return false;
            int column = position - GetLineStart(line);
            foreach (MarkdownTableCell cell in layout.Cells)
                if (column >= cell.Start && column < cell.Start + cell.Length) return false;
            return true;
        }

        internal bool TryGetMarkdownTaskMarkerForLine(int line, out MarkdownTaskMarker marker)
        {
            marker = default(MarkdownTaskMarker);
            if (IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int start = LeadingWhitespaceLength(value);

            MarkdownListMarker listMarker;
            if (TryGetMarkdownListMarkerForLine(line, out listMarker))
                start = listMarker.ContentStart;
            if (start + 2 >= value.Length || value[start] != '[' ||
                (value[start + 1] != ' ' && value[start + 1] != 'x' && value[start + 1] != 'X') ||
                value[start + 2] != ']') return false;
            if (start + 3 < value.Length && value[start + 3] != ' ' && value[start + 3] != '\t')
                return false;

            marker = new MarkdownTaskMarker(start, start + 3, value[start + 1] == 'x' || value[start + 1] == 'X');
            return true;
        }

        internal bool MarkdownTaskMarkerIsVisible(int line, MarkdownTaskMarker marker)
        {
            int lineStart = GetLineStart(line);
            int start = lineStart + marker.Start;
            int end = lineStart + marker.End;
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal List<MarkdownLinkSpan> GetMarkdownLinksForLine(int line)
        {
            var links = new List<MarkdownLinkSpan>();
            if (IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return links;
            string value = _lineCache[line].TrimEnd('\r');
            for (int start = 0; start < value.Length; start++)
            {
                if (value[start] != '[' || (start > 0 && value[start - 1] == '!')) continue;
                int labelEnd = FindUnescapedCharacter(value, ']', start + 1);
                if (labelEnd < 0 || labelEnd + 1 >= value.Length || value[labelEnd + 1] != '(') continue;
                int targetEnd = FindUnescapedCharacter(value, ')', labelEnd + 2);
                if (targetEnd < 0) continue;
                bool insideCode = false;
                foreach (MarkdownInlineCodeSpan code in GetMarkdownInlineCodeSpansForLine(line))
                    if (start >= code.ContentStart - code.DelimiterLength &&
                        start < code.ContentStart + code.ContentLength + code.DelimiterLength)
                    {
                        insideCode = true;
                        break;
                    }
                if (insideCode) continue;
                links.Add(new MarkdownLinkSpan(start, start + 1, labelEnd - start - 1,
                    labelEnd + 2, targetEnd - labelEnd - 2, targetEnd + 1));
                start = targetEnd;
            }
            return links;
        }

        private static int FindUnescapedCharacter(string value, char target, int start)
        {
            for (int position = start; position < value.Length; position++)
            {
                if (value[position] != target) continue;
                int backslashes = 0;
                for (int previous = position - 1; previous >= 0 && value[previous] == '\\'; previous--) backslashes++;
                if (backslashes % 2 == 0) return position;
            }
            return -1;
        }

        internal bool MarkdownLinkMarkersAreVisible(int line, MarkdownLinkSpan span)
        {
            int lineStart = GetLineStart(line);
            int start = lineStart + span.Start;
            int end = lineStart + span.End;
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal void ApplyMarkdownLinkStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (MarkdownLinkSpan span in GetMarkdownLinksForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.LabelStart ||
                         sourceColumns[index] >= span.LabelStart + span.LabelLength)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.LabelStart &&
                        sourceColumns[index] < span.LabelStart + span.LabelLength) index++;
                    if (index > start)
                    {
                        formatted.SetForegroundBrush(MarkdownLinkForeground, start, index - start);
                        formatted.SetTextDecorations(TextDecorations.Underline, start, index - start);
                    }
                }
            }
        }

        internal bool TryGetMarkdownMediaBlockForLine(int line, out MarkdownMediaBlock block)
        {
            block = default(MarkdownMediaBlock);
            if (IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int start = LeadingWhitespaceLength(value);
            string content = value.Substring(start).Trim();
            if (content.Length == 0) return false;

            bool imageSyntax = content.StartsWith("![", StringComparison.Ordinal);
            bool linkSyntax = content.StartsWith("[", StringComparison.Ordinal);
            if (imageSyntax || linkSyntax)
            {
                int labelStart = imageSyntax ? 2 : 1;
                int labelEnd = FindUnescapedCharacter(content, ']', labelStart);
                if (labelEnd < 0 || labelEnd + 1 >= content.Length || content[labelEnd + 1] != '(') return false;
                int targetEnd = FindUnescapedCharacter(content, ')', labelEnd + 2);
                if (targetEnd < 0 || content.Substring(targetEnd + 1).Trim().Length != 0) return false;
                string source = UnwrapMarkdownDestination(content.Substring(labelEnd + 2, targetEnd - labelEnd - 2));
                bool isVideo = IsMarkdownVideoSource(source);
                if (!imageSyntax && !isVideo) return false;
                string altText = content.Substring(labelStart, labelEnd - labelStart);
                block = new MarkdownMediaBlock(start, value.Length, source, altText, isVideo);
                return true;
            }

            Match htmlMedia = Regex.Match(content,
                @"(?is)^<(video|img)\b[^>]*\bsrc\s*=\s*(?:""(?<double>[^""]+)""|'(?<single>[^']+)')[^>]*>.*$");
            if (htmlMedia.Success)
            {
                string source = htmlMedia.Groups["double"].Success
                    ? htmlMedia.Groups["double"].Value : htmlMedia.Groups["single"].Value;
                bool isVideo = htmlMedia.Groups[1].Value.Equals("video", StringComparison.OrdinalIgnoreCase);
                block = new MarkdownMediaBlock(start, value.Length, source, string.Empty, isVideo);
                return true;
            }
            return false;
        }

        internal bool MarkdownMediaSourceIsVisible(int line, MarkdownMediaBlock block)
        {
            int lineStart = GetLineStart(line);
            int start = lineStart + block.Start;
            int end = lineStart + block.End;
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal ImageSource GetMarkdownImageSource(string source)
        {
            ImageSource cached;
            if (_markdownImageCache.TryGetValue(source, out cached)) return cached;
            ImageSource result = null;
            Uri uri = ResolveMarkdownMediaUri(source);
            if (uri != null)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnDemand;
                    image.UriSource = uri;
                    image.DownloadCompleted += (sender, e) => InvalidateEditor();
                    image.DownloadFailed += (sender, e) => InvalidateEditor();
                    image.EndInit();
                    result = image;
                }
                catch
                {
                    result = null;
                }
            }
            _markdownImageCache[source] = result;
            return result;
        }

        private static Uri ResolveMarkdownMediaUri(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            string path = Uri.UnescapeDataString(source.Trim());
            Uri uri;
            if (Uri.TryCreate(path, UriKind.Absolute, out uri) &&
                (uri.IsFile || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return uri;
            try
            {
                return new Uri(Path.GetFullPath(path));
            }
            catch
            {
                return null;
            }
        }

        private static string UnwrapMarkdownDestination(string destination)
        {
            string value = destination.Trim();
            if (value.Length >= 2 && value[0] == '<' && value[value.Length - 1] == '>')
                value = value.Substring(1, value.Length - 2);
            int titleStart = value.IndexOfAny(new[] { ' ', '\t' });
            if (titleStart >= 0) value = value.Substring(0, titleStart);
            return System.Net.WebUtility.HtmlDecode(value);
        }

        private static bool IsMarkdownVideoSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            string path;
            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri)) path = uri.AbsolutePath;
            else path = source;
            return Regex.IsMatch(path, @"\.(mp4|m4v|mov|wmv|avi|mpg|mpeg|webm|mkv|ogv)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal void RebuildMarkdownVideoPlayers()
        {
            if (_markdownMediaCanvas == null) return;
            foreach (MarkdownVideoPlayer player in _markdownVideoPlayers.Values) player.Dispose();
            _markdownVideoPlayers.Clear();
            _markdownMediaCanvas.Children.Clear();
            UpdateMarkdownVideoPlayerLayout();
        }

        internal void UpdateMarkdownVideoPlayerLayout()
        {
            if (_markdownMediaCanvas == null) return;
            var visibleVideoLines = new HashSet<int>();
            if (_visibleLines.Length > 0 && _scroll != null)
            {
                int firstRow = VisibleRowAtY(_scroll.VerticalOffset);
                int lastRow = VisibleRowAtY(_scroll.VerticalOffset + Math.Max(LineHeight, _scroll.ViewportHeight));
                for (int row = firstRow; row <= lastRow; row++)
                {
                    int line = DocumentLineAtVisibleRow(row);
                    MarkdownMediaBlock block;
                    if (TryGetMarkdownMediaBlockForLine(line, out block) && block.IsVideo &&
                        !MarkdownMediaSourceIsVisible(line, block))
                        visibleVideoLines.Add(line);
                }
            }

            var existingLines = new List<int>(_markdownVideoPlayers.Keys);
            foreach (int line in existingLines)
            {
                if (visibleVideoLines.Contains(line)) continue;
                MarkdownVideoPlayer obsoletePlayer = _markdownVideoPlayers[line];
                obsoletePlayer.Dispose();
                _markdownMediaCanvas.Children.Remove(obsoletePlayer);
                _markdownVideoPlayers.Remove(line);
            }
            foreach (int line in visibleVideoLines)
            {
                if (_markdownVideoPlayers.ContainsKey(line)) continue;
                MarkdownMediaBlock block;
                if (!TryGetMarkdownMediaBlockForLine(line, out block)) continue;
                Uri uri = ResolveMarkdownMediaUri(block.Source);
                if (uri == null) continue;
                var player = new MarkdownVideoPlayer(uri);
                _markdownVideoPlayers[line] = player;
                _markdownMediaCanvas.Children.Add(player);
            }

            foreach (KeyValuePair<int, MarkdownVideoPlayer> entry in _markdownVideoPlayers)
            {
                int line = entry.Key;
                MarkdownMediaBlock block;
                if (!TryGetMarkdownMediaBlockForLine(line, out block)) continue;
                MarkdownVideoPlayer player = entry.Value;
                Canvas.SetLeft(player, TextOriginForLine(line) + MeasureTextWidth(_lineCache[line].Substring(0, block.Start)));
                Canvas.SetTop(player, TopInset + VisibleRowTop(VisibleRowForDocumentLine(line)) + 2);
                player.Visibility = Visibility.Visible;
            }
        }

        private static string UnwrapMarkdownDestinationAndTitle(string destination)
        {
            return UnwrapMarkdownDestination(destination);
        }

        private sealed class MarkdownVideoPlayer : Grid
        {
            private readonly MediaElement _media;
            private readonly Button _playButton;
            private bool _playing;

            public MarkdownVideoPlayer(Uri source)
            {
                Width = MarkdownVideoWidth;
                Height = MarkdownVideoHeight;
                Background = Brushes.Black;
                ClipToBounds = true;
                _media = new MediaElement
                {
                    Source = source,
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Close,
                    Stretch = Stretch.Uniform,
                    Volume = 0.8
                };
                Children.Add(_media);
                var controls = new Border
                {
                    Height = 42,
                    Background = new SolidColorBrush(Color.FromArgb(220, 24, 28, 34)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                var panel = new DockPanel();
                _playButton = new Button
                {
                    Content = "▶",
                    MinWidth = 36,
                    Padding = new Thickness(6, 1, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _playButton.Click += (sender, args) => TogglePlayback();
                var label = new TextBlock
                {
                    Text = "Video",
                    Foreground = Brushes.White,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(_playButton);
                panel.Children.Add(label);
                controls.Child = panel;
                Children.Add(controls);
                _media.MediaEnded += (sender, args) => { _playing = false; _playButton.Content = "↻"; };
                _media.MediaFailed += (sender, args) =>
                {
                    _playing = false;
                    _playButton.Content = "!";
                    ToolTip = "Das Video konnte nicht geladen werden.";
                };
            }

            private void TogglePlayback()
            {
                if (_playing)
                {
                    _media.Pause();
                    _playButton.Content = "▶";
                    _playing = false;
                }
                else
                {
                    if (_media.Position >= _media.NaturalDuration.TimeSpan && _media.NaturalDuration.HasTimeSpan)
                        _media.Position = TimeSpan.Zero;
                    _media.Play();
                    _playButton.Content = "❚❚";
                    _playing = true;
                }
            }

            public void Pause()
            {
                if (!_playing) return;
                _media.Pause();
                _playButton.Content = "▶";
                _playing = false;
            }

            public void Dispose()
            {
                _media.Stop();
                _media.Close();
            }
        }
    }
}
