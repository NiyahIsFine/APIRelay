using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace APIRelay;

internal sealed class CodexOfficialModelCatalog
{
    private readonly Dictionary<string, CodexModelCapabilities> models;
    private static readonly Lazy<Task<CodexOfficialModelCatalog>> Installed = new(() => Task.Run(() =>
        LoadAsync(FindExecutables(), ReadBundledCatalogAsync)));

    private CodexOfficialModelCatalog(Dictionary<string, CodexModelCapabilities> models) => this.models = models;

    public static CodexOfficialModelCatalog Empty { get; } = new(new(StringComparer.Ordinal));
    public static Task<CodexOfficialModelCatalog> LoadInstalledAsync() => Installed.Value;
    public static CodexOfficialModelCatalog Current => LoadInstalledAsync().GetAwaiter().GetResult();
    public int Count => models.Count;

    internal static CodexOfficialModelCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("models", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
            throw new JsonException("Codex bundled catalog must contain a models array.");

        var models = new Dictionary<string, CodexModelCapabilities>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(slug.GetString())) continue;
            models.TryAdd(slug.GetString()!, CodexModelCapabilities.FromOfficialModel(entry));
        }
        return new(models);
    }

    public CodexModelCapabilities? Match(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        var match = LongestPrefix(modelId);
        if (match != null) return match.Clone();

        var slash = modelId.IndexOf('/');
        if (slash <= 0 || modelId.IndexOf('/', slash + 1) >= 0 ||
            !modelId[..slash].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return null;
        return LongestPrefix(modelId[(slash + 1)..])?.Clone();
    }

    private CodexModelCapabilities? LongestPrefix(string modelId) => models
        .Where(entry => modelId.StartsWith(entry.Key, StringComparison.Ordinal))
        .OrderByDescending(entry => entry.Key.Length)
        .Select(entry => entry.Value)
        .FirstOrDefault();

    internal static async Task<CodexOfficialModelCatalog> LoadAsync(
        IEnumerable<string> executables, Func<string, CancellationToken, Task<string?>> readCatalog)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var executable in executables.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (timeout.IsCancellationRequested) break;
            try
            {
                var json = await readCatalog(executable, timeout.Token).ConfigureAwait(false);
                if (json == null) continue;
                var catalog = Parse(json);
                if (catalog.Count > 0) return catalog;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                or Win32Exception or OperationCanceledException or InvalidOperationException)
            {
                Trace.TraceWarning("Could not read Codex bundled models from {0}: {1}", executable, ex.Message);
            }
        }
        return Empty;
    }

    private static async Task<string?> ReadBundledCatalogAsync(string executable, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                ArgumentList = { "debug", "models", "--bundled" }
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return process.ExitCode == 0 ? await stdout.ConfigureAwait(false) : null;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
        }
    }

    internal static IReadOnlyList<string> FindExecutables(string? searchPath = null, string? localAppData = null)
    {
        var candidates = new List<string>();
        foreach (var directory in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = directory.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(path)) continue;
            candidates.Add(Path.Combine(path, "codex.exe"));
            foreach (var platform in new[] { "x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc" })
            {
                candidates.Add(Path.Combine(path, "node_modules", "@openai", "codex", "vendor", platform, "codex", "codex.exe"));
                var package = platform.StartsWith("x86_64", StringComparison.Ordinal) ? "codex-win32-x64" : "codex-win32-arm64";
                candidates.Add(Path.Combine(path, "node_modules", "@openai", package, "vendor", platform, "codex", "codex.exe"));
                candidates.Add(Path.Combine(path, "node_modules", "@openai", "codex", "node_modules", "@openai",
                    package, "vendor", platform, "codex", "codex.exe"));
            }
        }

        var appBin = Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI", "Codex", "bin");
        candidates.Add(Path.Combine(appBin, "codex.exe"));
        try
        {
            if (Directory.Exists(appBin))
                candidates.AddRange(Directory.EnumerateDirectories(appBin)
                    .Select(directory => new FileInfo(Path.Combine(directory, "codex.exe")))
                    .Where(file => file.Exists).OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not discover app-bundled Codex: {0}", ex.Message);
        }
        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
