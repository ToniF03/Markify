using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Xml.Linq;

namespace Markify.Textify
{
    internal sealed class SyntaxDefinition
    {
        private readonly HashSet<string> _aliases;

        internal IList<SyntaxHighlightRule> Rules { get; private set; }

        private SyntaxDefinition(IEnumerable<string> aliases, IEnumerable<SyntaxHighlightRule> rules)
        {
            _aliases = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
            Rules = rules.ToList();
        }

        internal bool MatchesLanguage(string language)
        {
            return !string.IsNullOrWhiteSpace(language) && _aliases.Contains(language.Trim());
        }

        internal static SyntaxDefinition LoadFromResource(string resourceName)
        {
            try
            {
                Assembly assembly = typeof(SyntaxDefinition).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return new SyntaxDefinition(new string[0], new SyntaxHighlightRule[0]);
                    XDocument document = XDocument.Load(stream);
                    XElement root = document.Root;
                    if (root == null) return new SyntaxDefinition(new string[0], new SyntaxHighlightRule[0]);

                    IEnumerable<string> aliases = root.Element("Aliases") == null
                        ? new string[0]
                        : root.Element("Aliases").Elements("Alias").Select(element => element.Value.Trim())
                            .Where(value => value.Length > 0);
                    var rules = new List<SyntaxHighlightRule>();
                    XElement rulesElement = root.Element("Rules");
                    if (rulesElement != null)
                    {
                        foreach (XElement element in rulesElement.Elements("Rule"))
                        {
                            XAttribute pattern = element.Attribute("pattern");
                            XAttribute foreground = element.Attribute("foreground");
                            if (pattern == null || foreground == null) continue;
                            var color = (Color)ColorConverter.ConvertFromString(foreground.Value);
                            var brush = new SolidColorBrush(color);
                            if (brush.CanFreeze) brush.Freeze();
                            rules.Add(new SyntaxHighlightRule(pattern.Value, brush));
                        }
                    }
                    return new SyntaxDefinition(aliases, rules);
                }
            }
            catch
            {
                return new SyntaxDefinition(new string[0], new SyntaxHighlightRule[0]);
            }
        }
    }
}
