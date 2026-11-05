using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NAPS2.ExternalApplications;

/// <summary>
/// Converts an argument template like <c>--import "{file}" --pages {pageCount}</c> into a list of arguments.
///
/// The template is split into arguments first and the placeholders are substituted afterwards, so a value (e.g. a path
/// with spaces or quotes) can never turn into additional arguments.
///
/// Syntax: Whitespace separates arguments unless it's inside double quotes. Double quotes are removed, except that ""
/// inside quotes is a literal quote. Backslashes have no special meaning.
/// </summary>
public static class ExternalArgumentResolver
{
    private const string FILE = "file";
    private const string FILES = "files";
    private const string DIRECTORY = "directory";
    private const string FILENAME = "filename";
    private const string PAGE_COUNT = "pageCount";
    private const string FORMAT = "format";

    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z]+)\}");

    private static readonly HashSet<string> KnownPlaceholders = [FILE, FILES, DIRECTORY, FILENAME, PAGE_COUNT, FORMAT];

    // A program only receives the document if it's told at least one of these.
    private static readonly HashSet<string> DocumentPlaceholders = [FILE, FILES, DIRECTORY];

    public static IReadOnlyList<string> Tokenize(string template)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < template.Length && template[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (inQuotes)
        {
            throw new ExternalArgumentException(UiStrings.ExternalArgsUnterminatedQuote);
        }
        if (hasToken)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }

    /// <summary>
    /// Checks that the template can be resolved, so that problems are found before the document is exported.
    /// </summary>
    public static void Validate(string template)
    {
        bool referencesDocument = false;
        foreach (var token in Tokenize(template))
        {
            foreach (Match match in PlaceholderRegex.Matches(token))
            {
                var name = match.Groups[1].Value;
                if (!KnownPlaceholders.Contains(name))
                {
                    throw new ExternalArgumentException(
                        string.Format(UiStrings.ExternalArgsUnknownPlaceholder, match.Value));
                }
                if (name == FILES && token != match.Value)
                {
                    throw new ExternalArgumentException(UiStrings.ExternalArgsFilesNotAlone);
                }
                referencesDocument |= DocumentPlaceholders.Contains(name);
            }
        }
        if (!referencesDocument)
        {
            throw new ExternalArgumentException(UiStrings.ExternalArgsNoDocument);
        }
    }

    public static IReadOnlyList<string> Resolve(string template, ExternalExportResult export)
    {
        Validate(template);

        var firstFile = export.FilePaths[0];
        var values = new Dictionary<string, string>
        {
            [FILE] = firstFile,
            [DIRECTORY] = Path.GetDirectoryName(firstFile) ?? "",
            [FILENAME] = Path.GetFileName(firstFile),
            [PAGE_COUNT] = export.PageCount.ToString(CultureInfo.InvariantCulture),
            [FORMAT] = export.Format
        };

        var args = new List<string>();
        foreach (var token in Tokenize(template))
        {
            if (token == $"{{{FILES}}}")
            {
                args.AddRange(export.FilePaths);
            }
            else
            {
                // Regex.Replace makes a single pass, so a substituted value is never scanned for placeholders.
                args.Add(PlaceholderRegex.Replace(token, match => values[match.Groups[1].Value]));
            }
        }
        return args;
    }
}

public class ExternalArgumentException(string message) : Exception(message);
