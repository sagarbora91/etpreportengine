using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 A3. "The current definition of X" for text tests: the object file in <c>database/objects</c> when there is one,
/// otherwise the last migration that creates or alters X. Text tests switch to this only when their view next changes
/// (plan: no mass rewrite of the existing text tests).
/// </summary>
internal static class SchemaSourceText
{
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root (Etp.Reporting.slnx) was not found above " + AppContext.BaseDirectory);
    }

    public static string ObjectsDirectory() => Path.Combine(RepositoryRoot(), "database", "objects");

    public static string MigrationsDirectory() => Path.Combine(RepositoryRoot(), "database", "migrations");

    /// <summary>The object file of <paramref name="objectName"/> (schema.name), or null.</summary>
    public static string? ObjectFile(string objectName)
    {
        var directory = ObjectsDirectory();
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Length > 3
                && string.Equals(Path.GetFileNameWithoutExtension(path)[3..], objectName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The text that defines <paramref name="objectName"/> now: its object file, else the last migration that creates or alters it.</summary>
    public static string Current(string objectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        if (ObjectFile(objectName) is { } file) return File.ReadAllText(file);
        var dot = objectName.IndexOf('.', StringComparison.Ordinal);
        var schema = Regex.Escape(objectName[..dot]);
        var name = Regex.Escape(objectName[(dot + 1)..]);
        var pattern = new Regex(@"\b(CREATE|ALTER)(\s+OR\s+ALTER)?\s+(VIEW|PROC|PROCEDURE|FUNCTION)\s+\[?" + schema + @"\]?\s*\.\s*\[?" + name + @"\]?(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var last = Directory.EnumerateFiles(MigrationsDirectory(), "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .LastOrDefault(path => pattern.IsMatch(File.ReadAllText(path)));
        return last is null
            ? throw new InvalidOperationException($"No object file and no migration defines {objectName}.")
            : File.ReadAllText(last);
    }
}
