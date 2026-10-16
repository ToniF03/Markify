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

        internal struct MarkdownHardBreakMarker
        {
            public int Start;
            public int End;

            public MarkdownHardBreakMarker(int start, int end)
            {
                Start = start;
                End = end;
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
            public string Destination;

            public MarkdownLinkSpan(int start, int labelStart, int labelLength, int targetStart, int targetLength,
                int end, string destination)
            {
                Start = start;
                LabelStart = labelStart;
                LabelLength = labelLength;
                TargetStart = targetStart;
                TargetLength = targetLength;
                End = end;
                Destination = destination;
            }
        }

        internal struct MarkdownMathSpan
        {
            public int ContentStart;
            public int ContentLength;
            public int DelimiterLength;
            public int OpeningStart;
            public int ClosingStart;
            public bool IsDisplay;
            public bool IsDisplayBlockBody;

            public MarkdownMathSpan(int contentStart, int contentLength, int delimiterLength,
                int openingStart, int closingStart, bool isDisplay, bool isDisplayBlockBody)
            {
                ContentStart = contentStart;
                ContentLength = contentLength;
                DelimiterLength = delimiterLength;
                OpeningStart = openingStart;
                ClosingStart = closingStart;
                IsDisplay = isDisplay;
                IsDisplayBlockBody = isDisplayBlockBody;
            }
        }

        internal struct MarkdownMathCharacter
        {
            public char Value;
            public int SourceStart;
            public int SourceEnd;
            public bool IsFraction;
            public int ScriptKind;
            public List<MarkdownMathCharacter> Numerator;
            public List<MarkdownMathCharacter> Denominator;

            public MarkdownMathCharacter(char value, int sourceStart, int sourceEnd)
            {
                Value = value;
                SourceStart = sourceStart;
                SourceEnd = sourceEnd;
                IsFraction = false;
                ScriptKind = 0;
                Numerator = null;
                Denominator = null;
            }

            public MarkdownMathCharacter(List<MarkdownMathCharacter> numerator,
                List<MarkdownMathCharacter> denominator, int sourceStart, int sourceEnd)
            {
                Value = '\uFFFC';
                SourceStart = sourceStart;
                SourceEnd = sourceEnd;
                IsFraction = true;
                ScriptKind = 0;
                Numerator = numerator;
                Denominator = denominator;
            }
        }

        internal sealed class RenderedMathFractionLayout
        {
            public int DisplayStart;
            public int DisplayLength;
            public List<MarkdownMathCharacter> Numerator;
            public List<MarkdownMathCharacter> Denominator;
            public double Width;
            public double PlaceholderWidth;
            public double FontSize;
        }

        internal sealed class MarkdownMathPreviewLine
        {
            public int SourceLine;
            public List<MarkdownMathCharacter> Characters;
        }

        private void RebuildMarkdownMathBlocks()
        {
            _markdownMathBlocks = new List<MarkdownMathBlock>();
            _markdownMathBlockByLine = new int[_lineCache.Length];
            _markdownMathContainsFractionByLine = new bool[_lineCache.Length];
            _markdownMathFractionCheckedByLine = new bool[_lineCache.Length];
            for (int line = 0; line < _markdownMathBlockByLine.Length; line++)
                _markdownMathBlockByLine[line] = -1;
            if (!EnableMarkdownMath) return;

            for (int line = 0; line < _lineCache.Length; line++)
            {
                if (_markdownMathBlockByLine[line] >= 0 || IsMarkdownCodeFenceLine(line) ||
                    IsMarkdownIndentedCodeLine(line)) continue;
                int openingColumn;
                if (!TryGetMarkdownDisplayMathDelimiter(line, out openingColumn)) continue;
                int openingQuoteDepth = MarkdownQuoteDepthForLine(line);

                int closingLine = -1;
                int closingColumn = -1;
                for (int candidate = line + 1; candidate < _lineCache.Length; candidate++)
                {
                    if (IsMarkdownCodeFenceLine(candidate) || IsMarkdownIndentedCodeLine(candidate)) break;
                    int candidateColumn;
                    if (TryGetMarkdownDisplayMathDelimiter(candidate, out candidateColumn) &&
                        MarkdownQuoteDepthForLine(candidate) >= openingQuoteDepth)
                    {
                        closingLine = candidate;
                        closingColumn = candidateColumn;
                        break;
                    }
                }
                if (closingLine < 0) continue;

                int blockIndex = _markdownMathBlocks.Count;
                _markdownMathBlocks.Add(new MarkdownMathBlock(line, closingLine, openingColumn, closingColumn));
                for (int blockLine = line; blockLine <= closingLine; blockLine++)
                    _markdownMathBlockByLine[blockLine] = blockIndex;
                line = closingLine;
            }
        }

        private bool TryGetMarkdownDisplayMathDelimiter(int line, out int delimiterColumn)
        {
            delimiterColumn = -1;
            if (line < 0 || line >= _lineCache.Length) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int position = MarkdownQuotePrefixLengthForLine(line);
            while (position < value.Length && (value[position] == ' ' || value[position] == '\t')) position++;
            if (position + 2 > value.Length || value[position] != '$' || value[position + 1] != '$') return false;
            position += 2;
            while (position < value.Length && (value[position] == ' ' || value[position] == '\t')) position++;
            if (position != value.Length) return false;
            delimiterColumn = MarkdownQuotePrefixLengthForLine(line);
            while (delimiterColumn < value.Length &&
                (value[delimiterColumn] == ' ' || value[delimiterColumn] == '\t')) delimiterColumn++;
            return true;
        }

        private bool TryGetMarkdownMathBlock(int line, out MarkdownMathBlock block)
        {
            block = default(MarkdownMathBlock);
            if (!EnableMarkdownMath || line < 0 || line >= _markdownMathBlockByLine.Length) return false;
            int blockIndex = _markdownMathBlockByLine[line];
            if (blockIndex < 0 || blockIndex >= _markdownMathBlocks.Count) return false;
            block = _markdownMathBlocks[blockIndex];
            return true;
        }

        private bool IsMarkdownMathBlockDelimiterLine(int line)
        {
            MarkdownMathBlock block;
            return TryGetMarkdownMathBlock(line, out block) &&
                (line == block.OpeningLine || line == block.ClosingLine);
        }

        private bool IsMarkdownMathEnvironmentControlLine(int line)
        {
            MarkdownMathBlock block;
            if (!TryGetMarkdownMathBlock(line, out block) || IsMarkdownMathBlockDelimiterLine(line))
                return false;
            string value = _lineCache[line].TrimEnd('\r');
            int position = MarkdownQuotePrefixLengthForLine(line);
            while (position < value.Length && char.IsWhiteSpace(value[position])) position++;
            string remaining = value.Substring(position);
            if (!remaining.StartsWith("\\begin{", StringComparison.Ordinal) &&
                !remaining.StartsWith("\\end{", StringComparison.Ordinal)) return false;
            int groupStart = value.IndexOf('{', position);
            int groupEnd = value.IndexOf('}', groupStart + 1);
            if (groupStart < 0 || groupEnd < 0) return false;
            string environment = value.Substring(groupStart + 1,
                groupEnd - groupStart - 1).Trim();
            if (!IsSupportedMarkdownMathEnvironment(environment)) return false;
            int trailingStart = groupEnd + 1;
            while (trailingStart < value.Length && char.IsWhiteSpace(value[trailingStart])) trailingStart++;
            if (environment == "alignedat" && value.Substring(position).StartsWith("\\begin{",
                StringComparison.Ordinal))
            {
                if (!TryGetMathGroup(value, trailingStart, value.Length, out int countStart,
                    out int countEnd, out int afterCount) ||
                    !int.TryParse(value.Substring(countStart, countEnd - countStart), out _)) return false;
                trailingStart = afterCount;
            }
            for (int index = trailingStart; index < value.Length; index++)
                if (!char.IsWhiteSpace(value[index])) return false;
            return true;
        }

        internal bool MarkdownMathEnvironmentControlLineIsVisible(int line)
        {
            if (!IsMarkdownMathEnvironmentControlLine(line)) return false;
            int lineStart = GetLineStart(line);
            int lineEnd = GetLineEndAtLine(line);
            if (_caret >= lineStart && _caret <= lineEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= lineStart && caret <= lineEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < lineEnd && selection.Start + selection.Length > lineStart) return true;
            return false;
        }

        internal bool MarkdownMathBlockMarkersAreVisible(int line)
        {
            MarkdownMathBlock block;
            if (!TryGetMarkdownMathBlock(line, out block)) return false;
            int blockStart = GetLineStart(block.OpeningLine) + block.OpeningColumn;
            int blockEnd = GetLineStart(block.ClosingLine) + block.ClosingColumn + 2;
            if (_caret >= blockStart && _caret <= blockEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= blockStart && caret <= blockEnd) return true;
            for (int selectedLine = block.OpeningLine; selectedLine <= block.ClosingLine; selectedLine++)
                foreach (SelectionSpan selection in GetSelectionSpansForLine(selectedLine))
                    if (selection.Start < blockEnd && selection.Start + selection.Length > blockStart) return true;
            return false;
        }

        internal bool MarkdownMathSourceIsVisible(int line)
        {
            MarkdownMathBlock block;
            return TryGetMarkdownMathBlock(line, out block) && MarkdownMathBlockMarkersAreVisible(line);
        }

        internal bool MarkdownMathBlockPreviewIsVisible(int line)
        {
            MarkdownMathBlock block;
            return TryGetMarkdownMathBlock(line, out block) && line == block.ClosingLine &&
                MarkdownMathBlockMarkersAreVisible(line);
        }

        internal List<MarkdownMathPreviewLine> GetMarkdownMathPreviewLines(int closingLine)
        {
            var previewLines = new List<MarkdownMathPreviewLine>();
            MarkdownMathBlock block;
            if (!TryGetMarkdownMathBlock(closingLine, out block) || closingLine != block.ClosingLine)
                return previewLines;

            for (int line = block.OpeningLine + 1; line < block.ClosingLine; line++)
            {
                if (IsMarkdownMathEnvironmentControlLine(line)) continue;
                string source = _lineCache[line].TrimEnd('\r');
                int quotePrefix = MarkdownQuotePrefixLengthForLine(line);
                source = source.Substring(Math.Min(quotePrefix, source.Length)).Trim();
                previewLines.Add(new MarkdownMathPreviewLine
                {
                    SourceLine = line,
                    Characters = RenderMarkdownMath(source, 0)
                });
            }
            return previewLines;
        }

        internal double MarkdownMathSourceRowHeight(int line)
        {
            return Math.Ceiling(FontSize * 1.45);
        }

        internal double MarkdownMathBlockPreviewHeight(int closingLine)
        {
            List<MarkdownMathPreviewLine> lines = GetMarkdownMathPreviewLines(closingLine);
            if (lines.Count == 0) return 0;
            double height = 8 + 13 + 4 + 8;
            foreach (MarkdownMathPreviewLine line in lines)
            {
                double fontSize = FontSize * 1.08;
                bool containsFraction = line.Characters != null && line.Characters.Exists(character => character.IsFraction);
                height += Math.Ceiling(fontSize * (containsFraction ? 2.7 : 1.5)) + 2;
            }
            return height;
        }

        internal double MarkdownMathBlockPreviewWidth(int closingLine)
        {
            double contentWidth = 0;
            foreach (MarkdownMathPreviewLine line in GetMarkdownMathPreviewLines(closingLine))
                contentWidth = Math.Max(contentWidth, MeasureMarkdownMathSequenceWidth(line.Characters,
                    FontSize * 1.08));
            return Math.Max(220, contentWidth + 20);
        }

        internal List<MarkdownMathSpan> GetMarkdownMathSpansForLine(int line)
        {
            var spans = new List<MarkdownMathSpan>();
            if (!EnableMarkdownMath || line < 0 || line >= _lineCache.Length ||
                IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line)) return spans;
            string value = _lineCache[line].TrimEnd('\r');

            MarkdownMathBlock block;
            if (TryGetMarkdownMathBlock(line, out block))
            {
                if (line != block.OpeningLine && line != block.ClosingLine)
                {
                    int contentStart = MarkdownQuotePrefixLengthForLine(line);
                    int contentLength = Math.Max(0, value.Length - contentStart);
                    if (contentLength > 0)
                        spans.Add(new MarkdownMathSpan(contentStart, contentLength, 0, -1, -1, true, true));
                }
                return spans;
            }

            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf('$', searchFrom);
                if (opening < 0) break;
                if (IsMarkdownEscaped(value, opening) || IsMarkdownInlineCodeContentColumn(line, opening))
                {
                    searchFrom = opening + 1;
                    continue;
                }
                int delimiterLength = opening + 1 < value.Length && value[opening + 1] == '$' ? 2 : 1;
                if (opening > 0 && value[opening - 1] == '$' ||
                    opening + delimiterLength < value.Length && value[opening + delimiterLength] == '$')
                {
                    searchFrom = opening + delimiterLength;
                    continue;
                }
                int contentStart = opening + delimiterLength;
                if (contentStart >= value.Length ||
                    (delimiterLength == 1 && char.IsWhiteSpace(value[contentStart])))
                {
                    searchFrom = contentStart;
                    continue;
                }

                int closing = -1;
                int candidate = contentStart;
                while (candidate < value.Length)
                {
                    candidate = value.IndexOf('$', candidate);
                    if (candidate < 0) break;
                    if (IsMarkdownEscaped(value, candidate) || IsMarkdownInlineCodeContentColumn(line, candidate))
                    {
                        candidate++;
                        continue;
                    }
                    int candidateLength = candidate + 1 < value.Length && value[candidate + 1] == '$' ? 2 : 1;
                    if (candidateLength == delimiterLength &&
                        !(candidate > 0 && value[candidate - 1] == '$') &&
                        !(candidate + candidateLength < value.Length && value[candidate + candidateLength] == '$') &&
                        candidate > contentStart &&
                        (delimiterLength == 2 || !char.IsWhiteSpace(value[candidate - 1])))
                    {
                        closing = candidate;
                        break;
                    }
                    candidate += candidateLength;
                }
                if (closing < 0)
                {
                    searchFrom = contentStart;
                    continue;
                }

                spans.Add(new MarkdownMathSpan(contentStart, closing - contentStart, delimiterLength,
                    opening, closing, delimiterLength == 2, false));
                searchFrom = closing + delimiterLength;
            }
            return spans;
        }

        internal bool MarkdownMathMarkersAreVisible(int line, MarkdownMathSpan span)
        {
            if (span.DelimiterLength == 0 || span.OpeningStart < 0 || span.ClosingStart < 0) return false;
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + span.OpeningStart;
            int pairEnd = lineStart + span.ClosingStart + span.DelimiterLength;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        private bool IsMarkdownMathContentColumn(int line, int sourceColumn)
        {
            foreach (MarkdownMathSpan span in GetMarkdownMathSpansForLine(line))
                if (sourceColumn >= span.ContentStart && sourceColumn < span.ContentStart + span.ContentLength)
                    return true;
            return false;
        }

        private List<MarkdownScriptSpan> GetMarkdownMathScriptSpansForLine(int line)
        {
            var scripts = new List<MarkdownScriptSpan>();
            string value = _lineCache[line].TrimEnd('\r');
            foreach (MarkdownMathSpan math in GetMarkdownMathSpansForLine(line))
            {
                int end = math.ContentStart + math.ContentLength;
                for (int position = math.ContentStart; position < end; position++)
                {
                    char delimiter = value[position];
                    if ((delimiter != '^' && delimiter != '_') || IsMarkdownEscaped(value, position) ||
                        position + 1 >= end) continue;
                    int argumentStart = position + 1;
                    int argumentEnd;
                    if (TryGetMathGroup(value, argumentStart, end,
                        out int groupStart, out int groupEnd, out _))
                    {
                        argumentStart = groupStart;
                        argumentEnd = groupEnd;
                    }
                    else if (value[argumentStart] == '\\')
                    {
                        argumentEnd = argumentStart + 1;
                        if (argumentEnd < end && char.IsLetter(value[argumentEnd]))
                            while (argumentEnd < end && char.IsLetter(value[argumentEnd])) argumentEnd++;
                        else if (argumentEnd < end) argumentEnd++;
                    }
                    else
                    {
                        argumentEnd = argumentStart + 1;
                    }
                    if (argumentEnd > argumentStart)
                        scripts.Add(new MarkdownScriptSpan(argumentStart, argumentEnd - argumentStart,
                            delimiter == '^'));
                }
            }
            return scripts;
        }

        internal void ApplyMarkdownMathStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (!EnableMarkdownMath || MarkdownMathSourceIsVisible(line) ||
                sourceColumns == null || sourceColumns.Length == 0) return;
            List<MarkdownMathSpan> spans = GetMarkdownMathSpansForLine(line);
            for (int index = 0; index < sourceColumns.Length;)
            {
                MarkdownMathSpan active = default(MarkdownMathSpan);
                bool insideMath = false;
                foreach (MarkdownMathSpan span in spans)
                    if (sourceColumns[index] >= span.ContentStart &&
                        sourceColumns[index] < span.ContentStart + span.ContentLength)
                    {
                        active = span;
                        insideMath = true;
                        break;
                    }
                if (!insideMath)
                {
                    index++;
                    continue;
                }
                int start = index++;
                while (index < sourceColumns.Length)
                {
                    bool nextIsMath = false;
                    foreach (MarkdownMathSpan span in spans)
                        if (sourceColumns[index] >= span.ContentStart &&
                            sourceColumns[index] < span.ContentStart + span.ContentLength)
                        {
                            nextIsMath = true;
                            break;
                        }
                    if (!nextIsMath) break;
                    index++;
                }
                int length = index - start;
                formatted.SetFontFamily(new FontFamily("Cambria Math"), start, length);
                if (active.IsDisplay || active.IsDisplayBlockBody)
                    formatted.SetFontSize(Math.Max(1, FontSizeForLine(line) * 1.08), start, length);
            }
        }

        private List<MarkdownMathCharacter> RenderMarkdownMath(string value, int sourceOffset)
        {
            var output = new List<MarkdownMathCharacter>();
            RenderMarkdownMathRange(value, 0, value.Length, sourceOffset, output);
            return output;
        }

        private void RenderMarkdownMathRange(string value, int start, int end, int sourceOffset,
            List<MarkdownMathCharacter> output)
        {
            for (int position = start; position < end;)
            {
                char current = value[position];
                if (current == '\\' && position + 2 < end && value[position + 1] == '\\' &&
                    char.IsLetter(value[position + 2]))
                {
                    // Accept doubled command slashes commonly used when formulas are
                    // copied from escaped strings, e.g. \\pm and \\sqrt.
                    position++;
                    continue;
                }
                if (current == '\\' && position + 1 < end)
                {
                    int commandEnd = position + 1;
                    if (char.IsLetter(value[commandEnd]))
                    {
                        while (commandEnd < end && char.IsLetter(value[commandEnd])) commandEnd++;
                        string command = value.Substring(position + 1, commandEnd - position - 1);
                        int argumentStart;
                        int argumentEnd;
                        int afterArgument;
                        if ((command == "begin" || command == "end") &&
                            TryGetMathGroup(value, commandEnd, end, out argumentStart,
                                out argumentEnd, out afterArgument))
                        {
                            string environment = value.Substring(argumentStart,
                                argumentEnd - argumentStart).Trim();
                            if (IsSupportedMarkdownMathEnvironment(environment))
                            {
                                position = afterArgument;
                                if (command == "begin" && environment == "alignedat" &&
                                    TryGetMathGroup(value, position, end, out argumentStart,
                                        out argumentEnd, out afterArgument))
                                    position = afterArgument;
                                continue;
                            }
                        }
                        if (command == "frac" && TryRenderMarkdownMathFraction(value, commandEnd,
                            end, sourceOffset, position, output, out int afterFraction))
                        {
                            position = afterFraction;
                            continue;
                        }
                        if (TryGetMathAccent(command, out char accent) &&
                            TryGetMathGroup(value, commandEnd, end, out argumentStart,
                                out argumentEnd, out afterArgument))
                        {
                            var accented = new List<MarkdownMathCharacter>();
                            RenderMarkdownMathRange(value, argumentStart, argumentEnd, sourceOffset, accented);
                            foreach (MarkdownMathCharacter character in accented)
                            {
                                output.Add(character);
                                if (!char.IsWhiteSpace(character.Value))
                                    output.Add(new MarkdownMathCharacter(accent, character.SourceStart,
                                        character.SourceEnd));
                            }
                            position = afterArgument;
                            continue;
                        }
                        if (command == "sqrt")
                        {
                            int radicandPosition = commandEnd;
                            while (radicandPosition < end && char.IsWhiteSpace(value[radicandPosition])) radicandPosition++;
                            int rootIndexStart = -1;
                            int rootIndexEnd = -1;
                            if (radicandPosition < end && value[radicandPosition] == '[')
                            {
                                int rootIndexClose = value.IndexOf(']', radicandPosition + 1,
                                    end - radicandPosition - 1);
                                if (rootIndexClose >= 0)
                                {
                                    rootIndexStart = radicandPosition + 1;
                                    rootIndexEnd = rootIndexClose;
                                    radicandPosition = rootIndexClose + 1;
                                }
                            }
                            if (TryGetMathGroup(value, radicandPosition, end,
                                out argumentStart, out argumentEnd, out afterArgument))
                            {
                                if (rootIndexStart >= 0)
                                {
                                    var rootIndex = new List<MarkdownMathCharacter>();
                                    RenderMarkdownMathRange(value, rootIndexStart, rootIndexEnd, sourceOffset, rootIndex);
                                    foreach (MarkdownMathCharacter rootCharacter in rootIndex)
                                    {
                                        char superscript;
                                        output.Add(new MarkdownMathCharacter(
                                            TryTranslateMathSuperscript(rootCharacter.Value, out superscript)
                                                ? superscript : rootCharacter.Value,
                                            rootCharacter.SourceStart, rootCharacter.SourceEnd));
                                    }
                                }
                                output.Add(new MarkdownMathCharacter('\u221A', sourceOffset + position,
                                    sourceOffset + commandEnd));
                                var radicand = new List<MarkdownMathCharacter>();
                                RenderMarkdownMathRange(value, argumentStart, argumentEnd, sourceOffset, radicand);
                                if (radicand.Count > 1)
                                    output.Add(new MarkdownMathCharacter('(', sourceOffset + argumentStart - 1,
                                        sourceOffset + argumentStart));
                                output.AddRange(radicand);
                                if (radicand.Count > 1)
                                    output.Add(new MarkdownMathCharacter(')', sourceOffset + argumentEnd,
                                        sourceOffset + argumentEnd + 1));
                                position = afterArgument;
                                continue;
                            }
                        }
                        if ((command == "text" || command == "operatorname" || command == "mathrm" ||
                            command == "mathbf" || command == "mathit" || command == "mathsf" ||
                            command == "mathtt" || command == "mathcal" || command == "mathbb") &&
                            TryGetMathGroup(value, commandEnd, end, out argumentStart,
                                out argumentEnd, out afterArgument))
                        {
                            if (command == "mathbb" && argumentEnd - argumentStart == 1 &&
                                TryTranslateMathBlackboard(value[argumentStart], out char blackboard))
                                output.Add(new MarkdownMathCharacter(blackboard, sourceOffset + position,
                                    sourceOffset + afterArgument));
                            else
                                RenderMarkdownMathRange(value, argumentStart, argumentEnd, sourceOffset, output);
                            position = afterArgument;
                            continue;
                        }
                        if (TryTranslateMarkdownMathCommand(command, out string translated))
                        {
                            for (int character = 0; character < translated.Length; character++)
                                output.Add(new MarkdownMathCharacter(translated[character], sourceOffset + position,
                                    sourceOffset + commandEnd));
                            position = commandEnd;
                            continue;
                        }
                        if (command == "left" || command == "right" || command == "displaystyle" ||
                            command == "textstyle" || command == "scriptstyle" || command == "limits" ||
                            command == "nolimits")
                        {
                            position = commandEnd;
                            continue;
                        }
                        for (int character = position; character < commandEnd; character++)
                            output.Add(new MarkdownMathCharacter(value[character], sourceOffset + character,
                                sourceOffset + character + 1));
                        position = commandEnd;
                        continue;
                    }

                    char escaped = value[commandEnd];
                    if (escaped == '\\')
                    {
                        position += 2;
                        while (position < end && char.IsWhiteSpace(value[position])) position++;
                        if (position < end && value[position] == '[')
                        {
                            int spacingEnd = value.IndexOf(']', position + 1, end - position - 1);
                            if (spacingEnd >= 0) position = spacingEnd + 1;
                        }
                        continue;
                    }
                    if (escaped == '{' || escaped == '}' || escaped == '$' || escaped == '%' ||
                        escaped == '_' || escaped == '#' || escaped == '&' || escaped == '\\')
                    {
                        output.Add(new MarkdownMathCharacter(escaped, sourceOffset + position,
                            sourceOffset + position + 2));
                        position += 2;
                        continue;
                    }
                    if (escaped == ',' || escaped == ';' || escaped == ':' || escaped == '!')
                    {
                        if (escaped != '!') output.Add(new MarkdownMathCharacter(' ', sourceOffset + position,
                            sourceOffset + position + 2));
                        position += 2;
                        continue;
                    }
                    position++;
                    continue;
                }

                if (current == 'f' && position + 4 <= end &&
                    string.CompareOrdinal(value, position, "frac", 0, 4) == 0 &&
                    (position == start || !char.IsLetterOrDigit(value[position - 1])) &&
                    TryRenderMarkdownMathFraction(value, position + 4, end, sourceOffset,
                        position, output, out int afterBareFraction))
                {
                    position = afterBareFraction;
                    continue;
                }

                // In aligned TeX environments, an unescaped ampersand separates columns.
                // The editor lays out each source line independently, so omit the control
                // marker while keeping escaped \& available as a literal ampersand.
                if (current == '&')
                {
                    position++;
                    continue;
                }

                if ((current == '^' || current == '_') && position + 1 < end)
                {
                    int argumentStart = position + 1;
                    int argumentEnd;
                    int afterArgument;
                    if (TryGetMathGroup(value, argumentStart, end, out int groupStart,
                        out int groupEnd, out int afterGroup))
                    {
                        argumentStart = groupStart;
                        argumentEnd = groupEnd;
                        afterArgument = afterGroup;
                    }
                    else if (value[argumentStart] == '\\')
                    {
                        argumentEnd = argumentStart + 1;
                        if (argumentEnd < end && char.IsLetter(value[argumentEnd]))
                            while (argumentEnd < end && char.IsLetter(value[argumentEnd])) argumentEnd++;
                        else if (argumentEnd < end) argumentEnd++;
                        afterArgument = argumentEnd;
                    }
                    else
                    {
                        argumentEnd = argumentStart + 1;
                        afterArgument = argumentEnd;
                    }
                    var script = new List<MarkdownMathCharacter>();
                    RenderMarkdownMathRange(value, argumentStart, argumentEnd, sourceOffset, script);
                    foreach (MarkdownMathCharacter character in script)
                    {
                        if (character.IsFraction)
                        {
                            output.Add(character);
                            continue;
                        }
                        MarkdownMathCharacter scriptCharacter = character;
                        scriptCharacter.ScriptKind = current == '^' ? 1 : 2;
                        output.Add(scriptCharacter);
                    }
                    position = afterArgument;
                    continue;
                }

                output.Add(new MarkdownMathCharacter(current, sourceOffset + position,
                    sourceOffset + position + 1));
                position++;
            }
        }

        private bool TryRenderMarkdownMathFraction(string value, int numeratorPosition, int end,
            int sourceOffset, int commandStart, List<MarkdownMathCharacter> output, out int afterFraction)
        {
            afterFraction = numeratorPosition;
            if (!TryGetMathGroup(value, numeratorPosition, end, out int numeratorStart,
                out int numeratorEnd, out int afterNumerator)) return false;
            if (!TryGetMathGroup(value, afterNumerator, end, out int denominatorStart,
                out int denominatorEnd, out afterFraction)) return false;

            var numerator = new List<MarkdownMathCharacter>();
            var denominator = new List<MarkdownMathCharacter>();
            RenderMarkdownMathRange(value, numeratorStart, numeratorEnd, sourceOffset, numerator);
            RenderMarkdownMathRange(value, denominatorStart, denominatorEnd, sourceOffset, denominator);
            output.Add(new MarkdownMathCharacter(numerator, denominator,
                sourceOffset + commandStart, sourceOffset + afterFraction));
            return true;
        }

        private static bool IsSupportedMarkdownMathEnvironment(string environment)
        {
            switch (environment)
            {
                case "aligned":
                case "alignedat":
                case "align":
                case "align*":
                case "gathered":
                case "gather":
                case "gather*":
                case "split":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetMathGroup(string value, int position, int limit,
            out int contentStart, out int contentEnd, out int afterGroup)
        {
            contentStart = contentEnd = afterGroup = position;
            while (position < limit && char.IsWhiteSpace(value[position])) position++;
            if (position >= limit || value[position] != '{') return false;
            contentStart = position + 1;
            int depth = 1;
            for (int index = contentStart; index < limit; index++)
            {
                if (value[index] == '{' && !IsMarkdownEscaped(value, index)) depth++;
                else if (value[index] == '}' && !IsMarkdownEscaped(value, index) && --depth == 0)
                {
                    contentEnd = index;
                    afterGroup = index + 1;
                    return true;
                }
            }
            return false;
        }

        private static bool TryTranslateMarkdownMathCommand(string command, out string translated)
        {
            switch (command)
            {
                case "alpha": translated = "α"; break; case "beta": translated = "β"; break;
                case "gamma": translated = "γ"; break; case "delta": translated = "δ"; break;
                case "epsilon": translated = "ε"; break; case "varepsilon": translated = "ϵ"; break;
                case "zeta": translated = "ζ"; break; case "eta": translated = "η"; break;
                case "theta": translated = "θ"; break; case "vartheta": translated = "ϑ"; break;
                case "iota": translated = "ι"; break; case "kappa": translated = "κ"; break;
                case "lambda": translated = "λ"; break; case "mu": translated = "μ"; break;
                case "nu": translated = "ν"; break; case "xi": translated = "ξ"; break;
                case "omicron": translated = "ο"; break; case "pi": translated = "π"; break;
                case "varpi": translated = "ϖ"; break; case "rho": translated = "ρ"; break;
                case "varrho": translated = "ϱ"; break; case "sigma": translated = "σ"; break;
                case "varsigma": translated = "ς"; break; case "tau": translated = "τ"; break;
                case "upsilon": translated = "υ"; break; case "phi": translated = "φ"; break;
                case "varphi": translated = "ϕ"; break; case "chi": translated = "χ"; break;
                case "psi": translated = "ψ"; break; case "omega": translated = "ω"; break;
                case "Gamma": translated = "Γ"; break; case "Delta": translated = "Δ"; break;
                case "Theta": translated = "Θ"; break; case "Lambda": translated = "Λ"; break;
                case "Xi": translated = "Ξ"; break; case "Pi": translated = "Π"; break;
                case "Sigma": translated = "Σ"; break; case "Upsilon": translated = "Υ"; break;
                case "Phi": translated = "Φ"; break; case "Psi": translated = "Ψ"; break;
                case "Omega": translated = "Ω"; break; case "times": translated = "×"; break;
                case "cdot": translated = "·"; break; case "div": translated = "÷"; break;
                case "pm": translated = "±"; break; case "mp": translated = "∓"; break;
                case "le": case "leq": translated = "≤"; break;
                case "ge": case "geq": translated = "≥"; break;
                case "ne": case "neq": translated = "≠"; break;
                case "approx": translated = "≈"; break; case "equiv": translated = "≡"; break;
                case "infty": translated = "∞"; break; case "partial": translated = "∂"; break;
                case "nabla": translated = "∇"; break; case "forall": translated = "∀"; break;
                case "exists": translated = "∃"; break; case "in": translated = "∈"; break;
                case "notin": translated = "∉"; break; case "subset": translated = "⊂"; break;
                case "subseteq": translated = "⊆"; break; case "supset": translated = "⊃"; break;
                case "supseteq": translated = "⊇"; break; case "cup": translated = "∪"; break;
                case "cap": translated = "∩"; break; case "to": case "rightarrow": translated = "→"; break;
                case "leftarrow": translated = "←"; break; case "Rightarrow": translated = "⇒"; break;
                case "Leftarrow": translated = "⇐"; break; case "Leftrightarrow": translated = "⇔"; break;
                case "sum": translated = "∑"; break; case "prod": translated = "∏"; break;
                case "int": translated = "∫"; break; case "oint": translated = "∮"; break;
                case "sqrt": translated = "√"; break; case "ell": translated = "ℓ"; break;
                case "hbar": translated = "ℏ"; break; case "Re": translated = "ℜ"; break;
                case "Im": translated = "ℑ"; break; case "emptyset": translated = "∅"; break;
                case "land": translated = "∧"; break; case "lor": translated = "∨"; break;
                case "neg": translated = "¬"; break; case "propto": translated = "∝"; break;
                case "perp": translated = "⊥"; break; case "parallel": translated = "∥"; break;
                case "angle": translated = "∠"; break; case "therefore": translated = "∴"; break;
                case "cdots": translated = "⋯"; break; case "ldots": case "dots": translated = "…"; break;
                case "prime": translated = "′"; break; case "degree": translated = "°"; break;
                case "quad": translated = "　"; break; case "qquad": translated = "　　　"; break;
                case "sin": case "cos": case "tan": case "cot": case "sec": case "csc":
                case "arcsin": case "arccos": case "arctan": case "sinh": case "cosh":
                case "tanh": case "log": case "ln": case "lg": case "exp": case "lim":
                case "min": case "max": case "sup": case "inf": case "det": case "dim":
                case "ker": case "arg": case "gcd": case "Pr":
                    translated = command; break;
                default: translated = null; return false;
            }
            return true;
        }

        private static bool TryTranslateMathBlackboard(char value, out char translated)
        {
            switch (value)
            {
                case 'N': translated = 'ℕ'; return true;
                case 'Z': translated = 'ℤ'; return true;
                case 'Q': translated = 'ℚ'; return true;
                case 'R': translated = 'ℝ'; return true;
                case 'C': translated = 'ℂ'; return true;
                default: translated = value; return false;
            }
        }

        private static bool TryTranslateMathSuperscript(char value, out char translated)
        {
            switch (value)
            {
                case '0': translated = '⁰'; return true; case '1': translated = '¹'; return true;
                case '2': translated = '²'; return true; case '3': translated = '³'; return true;
                case '4': translated = '⁴'; return true; case '5': translated = '⁵'; return true;
                case '6': translated = '⁶'; return true; case '7': translated = '⁷'; return true;
                case '8': translated = '⁸'; return true; case '9': translated = '⁹'; return true;
                case '+': translated = '⁺'; return true; case '-': translated = '⁻'; return true;
                case '=': translated = '⁼'; return true; case '(': translated = '⁽'; return true;
                case ')': translated = '⁾'; return true; case 'n': translated = 'ⁿ'; return true;
                case 'i': translated = 'ⁱ'; return true; default: translated = value; return false;
            }
        }

        private static bool TryTranslateMathSubscript(char value, out char translated)
        {
            switch (value)
            {
                case '0': translated = '₀'; return true; case '1': translated = '₁'; return true;
                case '2': translated = '₂'; return true; case '3': translated = '₃'; return true;
                case '4': translated = '₄'; return true; case '5': translated = '₅'; return true;
                case '6': translated = '₆'; return true; case '7': translated = '₇'; return true;
                case '8': translated = '₈'; return true; case '9': translated = '₉'; return true;
                case '+': translated = '₊'; return true; case '-': translated = '₋'; return true;
                case '=': translated = '₌'; return true; case '(': translated = '₍'; return true;
                case ')': translated = '₎'; return true; case 'a': translated = 'ₐ'; return true;
                case 'e': translated = 'ₑ'; return true; case 'h': translated = 'ₕ'; return true;
                case 'i': translated = 'ᵢ'; return true; case 'k': translated = 'ₖ'; return true;
                case 'l': translated = 'ₗ'; return true; case 'm': translated = 'ₘ'; return true;
                case 'n': translated = 'ₙ'; return true; case 'o': translated = 'ₒ'; return true;
                case 'p': translated = 'ₚ'; return true; case 'r': translated = 'ᵣ'; return true;
                case 's': translated = 'ₛ'; return true; case 't': translated = 'ₜ'; return true;
                case 'u': translated = 'ᵤ'; return true; case 'v': translated = 'ᵥ'; return true;
                case 'x': translated = 'ₓ'; return true; default: translated = value; return false;
            }
        }

        private static bool TryGetMathAccent(string command, out char accent)
        {
            switch (command)
            {
                case "hat": accent = '\u0302'; return true;
                case "bar": case "overline": accent = '\u0305'; return true;
                case "tilde": accent = '\u0303'; return true;
                case "dot": accent = '\u0307'; return true;
                case "ddot": accent = '\u0308'; return true;
                case "vec": accent = '\u20D7'; return true;
                case "underline": accent = '\u0332'; return true;
                case "acute": accent = '\u0301'; return true;
                case "grave": accent = '\u0300'; return true;
                case "check": accent = '\u030C'; return true;
                case "breve": accent = '\u0306'; return true;
                default: accent = '\0'; return false;
            }
        }

        internal struct MarkdownFootnoteReference
        {
            public int Start;
            public int LabelStart;
            public int LabelLength;
            public int End;

            public MarkdownFootnoteReference(int start, int labelStart, int labelLength, int end)
            {
                Start = start;
                LabelStart = labelStart;
                LabelLength = labelLength;
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

        internal bool IsMarkdownReferenceDefinitionLine(int line)
        {
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return false;
            string value = _lineCache[line].TrimEnd('\r');
            return Regex.IsMatch(value,
                @"^ {0,3}\[(?!\^)[^\]]+\]:[ \t]*(?:<[^>\r\n]+>|\S+)",
                RegexOptions.CultureInvariant);
        }

        internal bool MarkdownReferenceDefinitionIsVisible(int line)
        {
            if (!IsMarkdownReferenceDefinitionLine(line)) return false;
            int start = GetLineStart(line);
            int end = GetLineEndAtLine(line);
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal bool TryGetMarkdownAlertForLine(int line, out string alertType,
            out int markerStart, out int markerEnd)
        {
            alertType = null;
            markerStart = markerEnd = 0;
            int quotePrefix = MarkdownQuotePrefixLengthForLine(line);
            if (quotePrefix <= 0 || line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line)) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int start = quotePrefix;
            while (start < value.Length && (value[start] == ' ' || value[start] == '\t')) start++;
            Match match = Regex.Match(value.Substring(start), @"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            alertType = match.Groups[1].Value.ToUpperInvariant();
            markerStart = start;
            markerEnd = start + match.Length;
            return true;
        }

        internal bool MarkdownAlertMarkersAreVisible(int line, int markerStart, int markerEnd)
        {
            return IsMarkdownBlockMarkerVisible(line, markerStart, markerEnd);
        }

        internal int MarkdownFootnoteDefinitionPrefixLength(int line)
        {
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return 0;
            Match match = Regex.Match(_lineCache[line].TrimEnd('\r'), @"^ {0,3}\[\^[^\]\s]+\]:[ \t]*");
            return match.Success ? match.Length : 0;
        }

        internal void RebuildMarkdownFootnoteDefinitionCache()
        {
            _markdownFootnoteDefinitions.Clear();
            for (int line = 0; line < _lineCache.Length; line++)
            {
                if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line)) continue;
                Match match = Regex.Match(_lineCache[line].TrimEnd('\r'),
                    @"^ {0,3}\[\^(?<label>[^\]\s]+)\]:");
                if (match.Success) _markdownFootnoteDefinitions.Add(match.Groups["label"].Value);
            }
        }

        internal List<MarkdownFootnoteReference> GetMarkdownFootnoteReferencesForLine(int line)
        {
            var references = new List<MarkdownFootnoteReference>();
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return references;
            string value = _lineCache[line].TrimEnd('\r');
            foreach (Match match in Regex.Matches(value, @"\[\^(?<label>[^\]\s]+)\]"))
            {
                if (IsEscaped(value, match.Index) || IsMarkdownInlineCodeContentColumn(line, match.Index)) continue;
                string label = match.Groups["label"].Value;
                if (!_markdownFootnoteDefinitions.Contains(label)) continue;
                Group labelGroup = match.Groups["label"];
                references.Add(new MarkdownFootnoteReference(match.Index, labelGroup.Index,
                    labelGroup.Length, match.Index + match.Length));
            }
            return references;
        }

        internal bool MarkdownFootnoteMarkersAreVisible(int line, MarkdownFootnoteReference reference)
        {
            return IsMarkdownBlockMarkerVisible(line, reference.Start, reference.End);
        }

        internal void ApplyMarkdownFootnoteStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (formatted == null || sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (MarkdownFootnoteReference reference in GetMarkdownFootnoteReferencesForLine(line))
            {
                if (MarkdownFootnoteMarkersAreVisible(line, reference)) continue;
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < reference.LabelStart ||
                         sourceColumns[index] >= reference.LabelStart + reference.LabelLength)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= reference.LabelStart &&
                        sourceColumns[index] < reference.LabelStart + reference.LabelLength) index++;
                    if (index > start) formatted.SetFontSize(Math.Max(8, FontSizeForLine(line) * 0.72), start, index - start);
                }
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
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return null;
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
            ApplyMarkdownFootnoteStyle(formatted, line, sourceColumns);
            ApplyMarkdownListMarkerStyle(formatted, line, sourceColumns);
            return Math.Max(0, formatted.WidthIncludingTrailingWhitespace +
                MarkdownMathFractionWidthAdjustment(segment, displayLength));
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
            if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return false;
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
            if (_taskCheckboxDisplaySuppressedAt == start && SelectionLength == 0 &&
                _caret >= start && _caret <= end) return false;
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal bool TryGetMarkdownTaskCheckboxBounds(int line, RenderedTextSegment segment,
            double textOrigin, double y, double rowHeight, bool requireRenderedCheckbox,
            out MarkdownTaskMarker marker, out Rect bounds)
        {
            marker = default(MarkdownTaskMarker);
            bounds = Rect.Empty;
            if (!TryGetMarkdownTaskMarkerForLine(line, out marker)) return false;
            bool markerVisible = MarkdownTaskMarkerIsVisible(line, marker);
            if (requireRenderedCheckbox && markerVisible) return false;
            int displayColumn = RenderedCharacterForSourceColumn(segment, marker.Start);
            if (displayColumn < 0) return false;
            double glyphStart = textOrigin + MeasureRenderedTextWidthForLine(segment, line, displayColumn);
            if (markerVisible)
            {
                int displayEnd = RenderedBoundaryForSourceColumn(segment, marker.End);
                double glyphEnd = textOrigin + MeasureRenderedTextWidthForLine(segment, line, displayEnd);
                bounds = new Rect(glyphStart, y, Math.Max(8, glyphEnd - glyphStart), rowHeight);
                return true;
            }
            double glyphWidth = MeasureRenderedTextWidthForLine(segment, line, displayColumn + 1) -
                MeasureRenderedTextWidthForLine(segment, line, displayColumn);
            double size = Math.Min(14, Math.Max(10, rowHeight - 4));
            bounds = new Rect(glyphStart + Math.Max(0, (glyphWidth - size) / 2),
                y + Math.Max(0, (rowHeight - size) / 2), size, size);
            return true;
        }

        internal bool TryGetMarkdownHardBreakMarkerForLine(int line, out MarkdownHardBreakMarker marker)
        {
            marker = default(MarkdownHardBreakMarker);
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return false;
            MarkdownTableLayout tableLayout;
            if (TryGetMarkdownTableLayout(line, out tableLayout)) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int end = value.Length;
            int trailingSpaces = 0;
            while (end - trailingSpaces - 1 >= 0 && value[end - trailingSpaces - 1] == ' ') trailingSpaces++;
            if (trailingSpaces >= 2 && trailingSpaces < value.Length)
            {
                marker = new MarkdownHardBreakMarker(value.Length - trailingSpaces, value.Length);
                return true;
            }
            if (end == 0 || value[end - 1] != '\\') return false;
            int backslashes = 0;
            for (int previous = end - 1; previous >= 0 && value[previous] == '\\'; previous--) backslashes++;
            if (backslashes % 2 == 0) return false;
            marker = new MarkdownHardBreakMarker(end - 1, end);
            return true;
        }

        internal bool MarkdownHardBreakMarkerIsVisible(int line, MarkdownHardBreakMarker marker)
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
            if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return links;
            string value = _lineCache[line].TrimEnd('\r');
            for (int start = 0; start < value.Length; start++)
            {
                if (value[start] != '[' || (start > 0 && value[start - 1] == '!') || IsEscaped(value, start)) continue;
                int labelEnd = FindUnescapedCharacter(value, ']', start + 1);
                if (labelEnd < 0) continue;
                bool insideCode = false;
                foreach (MarkdownInlineCodeSpan code in GetMarkdownInlineCodeSpansForLine(line))
                    if (start >= code.ContentStart - code.DelimiterLength &&
                        start < code.ContentStart + code.ContentLength + code.DelimiterLength)
                    {
                        insideCode = true;
                        break;
                    }
                if (insideCode) continue;

                int labelLength = labelEnd - start - 1;
                if (labelLength == 0) continue;
                if (labelEnd + 1 < value.Length && value[labelEnd + 1] == '(')
                {
                    int targetEnd = FindMarkdownLinkTargetEnd(value, labelEnd + 1);
                    if (targetEnd < 0) continue;
                    int targetStart = labelEnd + 2;
                    int targetLength = targetEnd - targetStart;
                    string destination = ParseMarkdownLinkDestination(value.Substring(targetStart, targetLength));
                    links.Add(new MarkdownLinkSpan(start, start + 1, labelLength,
                        targetStart, targetLength, targetEnd + 1, destination));
                    start = targetEnd;
                    continue;
                }

                string reference = null;
                int end = labelEnd + 1;
                int referenceStart = -1;
                int referenceLength = 0;
                if (end < value.Length && value[end] == '[')
                {
                    int referenceEnd = FindUnescapedCharacter(value, ']', end + 1);
                    if (referenceEnd >= 0)
                    {
                        referenceStart = end + 1;
                        referenceLength = referenceEnd - referenceStart;
                        reference = referenceLength == 0
                            ? value.Substring(start + 1, labelLength)
                            : value.Substring(referenceStart, referenceLength);
                        end = referenceEnd + 1;
                    }
                }
                else if (end < value.Length && value[end] == ':')
                {
                    continue;
                }
                else
                {
                    referenceStart = start + 1;
                    referenceLength = labelLength;
                    reference = value.Substring(referenceStart, referenceLength);
                }
                if (reference != null && reference.StartsWith("^", StringComparison.Ordinal)) continue;
                string referenceDestination;
                if (reference != null && TryGetMarkdownReferenceDestination(reference, out referenceDestination))
                {
                    links.Add(new MarkdownLinkSpan(start, start + 1, labelLength,
                        referenceStart, referenceLength, end, referenceDestination));
                    start = end - 1;
                }
            }
            foreach (Match match in Regex.Matches(value,
                @"<(?<angle>(?:https?://|mailto:)[^<>\s]+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,})>|(?<bare>(?:https?://|www\.)[^\s<>]+|(?<![\w.+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                bool angle = match.Groups["angle"].Success;
                Group targetGroup = angle ? match.Groups["angle"] : match.Groups["bare"];
                string label = targetGroup.Value;
                int labelStart = targetGroup.Index;
                int labelLength = targetGroup.Length;
                while (labelLength > 0 && ".,!?:;".IndexOf(label[labelLength - 1]) >= 0) labelLength--;
                while (labelLength > 0 && label[labelLength - 1] == ')')
                {
                    string candidate = label.Substring(0, labelLength);
                    int opens = 0, closes = 0;
                    foreach (char character in candidate)
                    {
                        if (character == '(') opens++;
                        else if (character == ')') closes++;
                    }
                    if (closes <= opens) break;
                    labelLength--;
                }
                if (labelLength == 0 || IsEscaped(value, match.Index)) continue;
                bool insideCode = false;
                foreach (MarkdownInlineCodeSpan code in GetMarkdownInlineCodeSpansForLine(line))
                    if (labelStart >= code.ContentStart - code.DelimiterLength &&
                        labelStart < code.ContentStart + code.ContentLength + code.DelimiterLength)
                    { insideCode = true; break; }
                if (insideCode) continue;
                int spanStart = angle ? match.Index : labelStart;
                int spanEnd = angle ? match.Index + match.Length : labelStart + labelLength;
                bool overlaps = false;
                foreach (MarkdownLinkSpan existing in links)
                    if (spanStart < existing.End && spanEnd > existing.Start) { overlaps = true; break; }
                if (overlaps) continue;
                string destination = value.Substring(labelStart, labelLength);
                if (destination.IndexOf('@') >= 0 &&
                    !destination.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !destination.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !destination.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    destination = "mailto:" + destination;
                links.Add(new MarkdownLinkSpan(spanStart, labelStart, labelLength,
                    labelStart, labelLength, spanEnd, destination));
            }
            return links;
        }

        private bool TryGetMarkdownReferenceDestination(string reference, out string destination)
        {
            destination = null;
            string normalized = Regex.Replace(reference.Trim(), @"\s+", " ").ToUpperInvariant();
            if (normalized.Length == 0) return false;
            for (int line = 0; line < _lineCache.Length; line++)
            {
                if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line)) continue;
                string value = _lineCache[line].TrimEnd('\r');
                Match match = Regex.Match(value,
                    @"^ {0,3}\[([^\]]+)\]:[ \t]*(?:<([^>\r\n]+)>|(\S+))",
                    RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                string defined = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ").ToUpperInvariant();
                if (!string.Equals(normalized, defined, StringComparison.Ordinal)) continue;
                destination = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                return destination.Length > 0;
            }
            return false;
        }

        private static string ParseMarkdownLinkDestination(string target)
        {
            target = target.Trim();
            if (target.Length == 0) return string.Empty;
            if (target[0] == '<')
            {
                int closingAngle = target.IndexOf('>');
                if (closingAngle > 0) return target.Substring(1, closingAngle - 1);
            }
            int depth = 0;
            char quote = '\0';
            for (int index = 0; index < target.Length; index++)
            {
                char character = target[index];
                if (character == '\\' && index + 1 < target.Length) { index++; continue; }
                if (quote != '\0')
                {
                    if (character == quote) quote = '\0';
                    continue;
                }
                if (character == '"' || character == '\'') { quote = character; continue; }
                if (character == '(') { depth++; continue; }
                if (character == ')' && depth > 0) { depth--; continue; }
                if (char.IsWhiteSpace(character) && depth == 0)
                    return target.Substring(0, index).Trim();
            }
            return target;
        }

        private static int FindMarkdownLinkTargetEnd(string value, int openParenthesis)
        {
            int depth = 0;
            bool inAngleDestination = false;
            char quote = '\0';
            for (int position = openParenthesis + 1; position < value.Length; position++)
            {
                char character = value[position];
                if (character == '\\' && position + 1 < value.Length) { position++; continue; }
                if (quote != '\0')
                {
                    if (character == quote) quote = '\0';
                    continue;
                }
                if (inAngleDestination)
                {
                    if (character == '>') inAngleDestination = false;
                    continue;
                }
                if (character == '<' && position == openParenthesis + 1)
                {
                    inAngleDestination = true;
                    continue;
                }
                if ((character == '"' || character == '\'') && depth == 0)
                {
                    quote = character;
                    continue;
                }
                if (character == '(') { depth++; continue; }
                if (character == ')')
                {
                    if (depth == 0) return position;
                    depth--;
                }
            }
            return -1;
        }

        private static bool IsEscaped(string value, int position)
        {
            int backslashes = 0;
            for (int previous = position - 1; previous >= 0 && value[previous] == '\\'; previous--) backslashes++;
            return backslashes % 2 != 0;
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
            if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return false;
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
                if (labelEnd < 0 || labelEnd + 1 >= content.Length) return false;
                string altText = content.Substring(labelStart, labelEnd - labelStart);
                string source;
                int end;
                if (content[labelEnd + 1] == '(')
                {
                    end = FindMarkdownLinkTargetEnd(content, labelEnd + 1);
                    if (end < 0) return false;
                    source = UnwrapMarkdownDestination(content.Substring(labelEnd + 2, end - labelEnd - 2));
                    end++;
                }
                else if (content[labelEnd + 1] == '[')
                {
                    int referenceEnd = FindUnescapedCharacter(content, ']', labelEnd + 2);
                    if (referenceEnd < 0) return false;
                    string reference = referenceEnd == labelEnd + 2 ? altText :
                        content.Substring(labelEnd + 2, referenceEnd - labelEnd - 2);
                    if (!TryGetMarkdownReferenceDestination(reference, out source)) return false;
                    end = referenceEnd + 1;
                }
                else return false;
                if (content.Substring(end).Trim().Length != 0) return false;
                bool isVideo = IsMarkdownVideoSource(source);
                if (!imageSyntax && !isVideo) return false;
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
