using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityAgentClient
{
    /// <summary>
    /// Converts a markdown string into a UI Toolkit VisualElement tree.
    /// Supports headings, code blocks, inline code, bold, italic, strikethrough,
    /// links, lists, blockquotes, and horizontal rules.
    /// </summary>
    internal static class MarkdownVisualBuilder
    {
        public static VisualElement Build(string markdown)
        {
            var root = new VisualElement();
            root.AddToClassList("md-root");

            if (string.IsNullOrEmpty(markdown)) return root;

            var blocks = ParseBlocks(markdown);
            foreach (var block in blocks)
                root.Add(BuildBlock(block));

            return root;
        }

        // ── Block types ──

        enum BlockKind
        {
            Paragraph, Heading, CodeBlock, UnorderedList, OrderedList, BlockQuote, HorizontalRule
        }

        class Block
        {
            public BlockKind Kind;
            public string Content;
            public string Language;   // code blocks
            public int Level;         // heading level (1-6) or indent level
        }

        // ── Parsing ──

        static List<Block> ParseBlocks(string markdown)
        {
            var blocks = new List<Block>();
            var lines = markdown.Split('\n');
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                // Blank line
                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                var trimmed = line.TrimStart();

                // Fenced code block
                if (trimmed.StartsWith("```"))
                {
                    var langMatch = Regex.Match(trimmed, @"^```(\w*)");
                    var lang = langMatch.Groups[1].Value;
                    i++;
                    var codeLines = new List<string>();
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        codeLines.Add(lines[i]);
                        i++;
                    }
                    if (i < lines.Length) i++; // skip closing ```
                    blocks.Add(new Block { Kind = BlockKind.CodeBlock, Content = string.Join("\n", codeLines), Language = lang });
                    continue;
                }

                // Horizontal rule
                if (Regex.IsMatch(line.Trim(), @"^(\*{3,}|-{3,}|_{3,})$"))
                {
                    blocks.Add(new Block { Kind = BlockKind.HorizontalRule });
                    i++;
                    continue;
                }

                // Heading
                var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                if (headingMatch.Success)
                {
                    blocks.Add(new Block
                    {
                        Kind = BlockKind.Heading,
                        Level = headingMatch.Groups[1].Length,
                        Content = headingMatch.Groups[2].Value
                    });
                    i++;
                    continue;
                }

                // Ordered list item
                var olMatch = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.+)$");
                if (olMatch.Success)
                {
                    blocks.Add(new Block
                    {
                        Kind = BlockKind.OrderedList,
                        Level = olMatch.Groups[1].Length / 2,
                        Content = olMatch.Groups[3].Value
                    });
                    i++;
                    continue;
                }

                // Unordered list item
                var ulMatch = Regex.Match(line, @"^(\s*)([-*+])\s+(.+)$");
                if (ulMatch.Success)
                {
                    blocks.Add(new Block
                    {
                        Kind = BlockKind.UnorderedList,
                        Level = ulMatch.Groups[1].Length / 2,
                        Content = ulMatch.Groups[3].Value
                    });
                    i++;
                    continue;
                }

                // Block quote
                if (trimmed.StartsWith(">"))
                {
                    var quoteLines = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">"))
                    {
                        var qLine = lines[i].TrimStart();
                        quoteLines.Add(qLine.Length > 1 ? qLine.Substring(1).TrimStart() : "");
                        i++;
                    }
                    blocks.Add(new Block { Kind = BlockKind.BlockQuote, Content = string.Join("\n", quoteLines) });
                    continue;
                }

                // Paragraph — collect consecutive non-special lines
                var paraLines = new List<string>();
                while (i < lines.Length)
                {
                    var pLine = lines[i];
                    if (string.IsNullOrWhiteSpace(pLine)) { i++; break; }
                    var pt = pLine.TrimStart();
                    if (pt.StartsWith("#") || pt.StartsWith("```") || pt.StartsWith(">") ||
                        Regex.IsMatch(pLine.Trim(), @"^[-*+]\s") ||
                        Regex.IsMatch(pLine.Trim(), @"^\d+\.\s") ||
                        Regex.IsMatch(pLine.Trim(), @"^(\*{3,}|-{3,}|_{3,})$"))
                        break;
                    paraLines.Add(pLine);
                    i++;
                }
                if (paraLines.Count > 0)
                    blocks.Add(new Block { Kind = BlockKind.Paragraph, Content = string.Join(" ", paraLines) });
            }

            return blocks;
        }

        // ── Building VisualElements ──

        static VisualElement BuildBlock(Block block)
        {
            switch (block.Kind)
            {
                case BlockKind.Heading:
                    return BuildHeading(block.Content, block.Level);
                case BlockKind.CodeBlock:
                    return BuildCodeBlock(block.Content, block.Language);
                case BlockKind.UnorderedList:
                    return BuildListItem("•", block.Content, block.Level);
                case BlockKind.OrderedList:
                    return BuildListItem(null, block.Content, block.Level);
                case BlockKind.BlockQuote:
                    return BuildBlockQuote(block.Content);
                case BlockKind.HorizontalRule:
                    return BuildHorizontalRule();
                case BlockKind.Paragraph:
                default:
                    return BuildParagraph(block.Content);
            }
        }

        static VisualElement BuildHeading(string text, int level)
        {
            var label = new Label(ProcessInline(text));
            label.enableRichText = true;
            label.AddToClassList("md-heading");
            label.AddToClassList($"md-h{Mathf.Clamp(level, 1, 6)}");
            return label;
        }

        static VisualElement BuildParagraph(string text)
        {
            var label = new Label(ProcessInline(text));
            label.enableRichText = true;
            label.AddToClassList("md-paragraph");
            return label;
        }

        static VisualElement BuildCodeBlock(string code, string language)
        {
            var container = new VisualElement();
            container.AddToClassList("md-code-block");

            if (!string.IsNullOrEmpty(language))
            {
                var langLabel = new Label(language);
                langLabel.AddToClassList("md-code-lang");
                container.Add(langLabel);
            }

            var codeField = new TextField();
            codeField.multiline = true;
            codeField.isReadOnly = true;
            codeField.value = code;
            codeField.AddToClassList("md-code-content");
            container.Add(codeField);

            return container;
        }

        static VisualElement BuildListItem(string bullet, string text, int indent)
        {
            var row = new VisualElement();
            row.AddToClassList("md-list-item");
            row.style.marginLeft = indent * 16;

            var marker = new Label(bullet ?? "1.");
            marker.AddToClassList("md-list-marker");
            row.Add(marker);

            var content = new Label(ProcessInline(text));
            content.enableRichText = true;
            content.AddToClassList("md-list-content");
            row.Add(content);

            return row;
        }

        static VisualElement BuildBlockQuote(string text)
        {
            var container = new VisualElement();
            container.AddToClassList("md-blockquote");

            var label = new Label(ProcessInline(text));
            label.enableRichText = true;
            label.AddToClassList("md-paragraph");
            container.Add(label);

            return container;
        }

        static VisualElement BuildHorizontalRule()
        {
            var hr = new VisualElement();
            hr.AddToClassList("md-hr");
            return hr;
        }

        // ── Inline processing → Unity rich text tags ──

        static string ProcessInline(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Inline code — process first to protect content inside backticks
            text = Regex.Replace(text, @"`([^`]+)`", m =>
                $"<color=#CE9178>{Escape(m.Groups[1].Value)}</color>");

            // Bold + italic
            text = Regex.Replace(text, @"(\*{3}|_{3})(.+?)\1", "<b><i>$2</i></b>");

            // Bold
            text = Regex.Replace(text, @"(\*{2}|_{2})(.+?)\1", "<b>$2</b>");

            // Italic
            text = Regex.Replace(text, @"(?<!\w)(\*|_)(.+?)\1(?!\w)", "<i>$2</i>");

            // Strikethrough
            text = Regex.Replace(text, @"~~(.+?)~~", "<s>$1</s>");

            // Links [text](url) — show text as underlined
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^\)]+)\)", "<u>$1</u>");

            return text;
        }

        static string Escape(string text)
        {
            return text.Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
