using System.Globalization;
using System.Xml.Linq;

namespace Crap4Net;

/// <summary>
/// Reads Cobertura XML as written by Microsoft.Testing.Extensions.CodeCoverage,
/// Microsoft.CodeCoverage (Format=cobertura) and coverlet.
/// </summary>
internal static class CoberturaParser
{
    public static CoverageData ParseFile(string reportPath)
    {
        try
        {
            return Parse(XDocument.Load(reportPath), Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        }
        catch (System.Xml.XmlException e)
        {
            throw new CrapFailureException($"Cannot read coverage report {reportPath}: {e.Message}");
        }
    }

    /// <param name="reportDirectory">Base for relative filenames when the report lists no sources.</param>
    public static CoverageData Parse(XDocument report, string reportDirectory)
    {
        var sources = report.Descendants("source")
            .Select(s => s.Value.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var data = new CoverageData();
        foreach (var @class in report.Descendants("class"))
        {
            if (@class.Attribute("filename")?.Value is not { Length: > 0 } filename)
                continue;
            var path = Resolve(filename, sources, reportDirectory);
            // Lines appear both under <methods> and directly under the class; merging takes the max.
            foreach (var line in @class.Descendants("line"))
            {
                if (TryParse(line.Attribute("number"), out var number) && TryParse(line.Attribute("hits"), out var hits))
                    data.AddLine(path, (int)number, hits);
            }
        }
        return data;
    }

    private static string Resolve(string filename, IReadOnlyList<string> sources, string reportDirectory)
    {
        if (Path.IsPathRooted(filename))
            return filename;
        var candidates = sources.Count > 0 ? sources : [reportDirectory];
        var paths = candidates.Select(source => Path.Combine(source, filename)).ToList();
        return paths.FirstOrDefault(File.Exists) ?? paths[0];
    }

    private static bool TryParse(XAttribute? attribute, out long value) =>
        long.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
