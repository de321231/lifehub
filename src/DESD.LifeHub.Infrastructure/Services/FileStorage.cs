using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Ablage für hochgeladene Dateien. Speicherschlüssel werden serverseitig erzeugt;
/// Benutzereingaben (Dateiname) fliessen niemals in Pfade ein. Damit ist ein
/// Path-Traversal ausgeschlossen.
/// </summary>
public interface IFileStorage
{
    /// <summary>Speichert einen Datenstrom und liefert den erzeugten Schlüssel.</summary>
    Task<string> SaveAsync(Stream content, CancellationToken ct = default);

    /// <summary>Öffnet eine gespeicherte Datei zum Lesen. Null, wenn sie fehlt.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Löscht eine Datei; fehlende Dateien sind kein Fehler.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        var configured = options.Value.AttachmentsPath;
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var folder = Path.Combine(now.ToString("yyyy"), now.ToString("MM"));
        var fileName = $"{Guid.NewGuid():N}.bin";
        var key = $"{now:yyyy}/{now:MM}/{fileName}";

        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(_root, now.ToString("yyyy"), now.ToString("MM"), fileName);
        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        return key;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolveSafePath(storageKey);
        if (path is null || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolveSafePath(storageKey);
        if (path is not null && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Aufräumen ist nachrangig; ein Fehler darf den Vorgang nicht abbrechen.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Wandelt einen Schlüssel in einen Pfad um und verhindert Ausbrüche aus dem Wurzelverzeichnis.</summary>
    private string? ResolveSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(_root, storageKey));
        var rootFull = Path.GetFullPath(_root);

        return combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || string.Equals(combined, rootFull, StringComparison.Ordinal)
            ? combined
            : null;
    }
}
