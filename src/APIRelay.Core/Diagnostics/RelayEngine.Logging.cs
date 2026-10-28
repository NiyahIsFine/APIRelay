using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace APIRelay;

internal sealed partial class RelayEngine
{
    private string EnsureProtocolLogFile()
    {
        lock (protocolLogLock)
        {
            Directory.CreateDirectory(logsDirectory);
            var activePath = GetActiveProtocolLogPath(logsDirectory, ProtocolLogFileCount);
            if (activePath == null)
            {
                activePath = GetProtocolLogPath(logsDirectory, 1);
                File.WriteAllText(activePath, string.Empty, new UTF8Encoding(false));
            }

            return activePath;
        }
    }

    private void AppendProtocolLog(string requestId, ProtocolTraceDirection direction, ApiRouteKind protocol, byte[] body)
    {
        AppendProtocolLog(requestId, direction, protocol, Encoding.UTF8.GetString(body));
    }

    private void AppendProtocolLog(string requestId, ProtocolTraceDirection direction, ApiRouteKind protocol, string body)
    {
        if (!protocolTraceVisible)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Request={requestId}; Direction={direction}; Protocol={protocol}");
            builder.AppendLine(body);
            builder.AppendLine();

            lock (protocolLogLock)
            {
                AppendRotatingProtocolLog(
                    logsDirectory,
                    builder.ToString(),
                    ProtocolLogFileMaxBytes,
                    ProtocolLogFileCount);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool IsProtocolLogFile(string path)
    {
        if (path.Equals(protocolLogPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.StartsWith("protocol-trace-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fileName["protocol-trace-".Length..], out var index)
            && index is >= 2 and <= ProtocolLogFileCount;
    }

    private static void AppendRotatingProtocolLog(string directory, string content, long maxFileBytes, int maxFileCount)
    {
        Directory.CreateDirectory(directory);
        var bytes = new UTF8Encoding(false).GetBytes(content);
        var activePath = GetActiveProtocolLogPath(directory, maxFileCount);
        var activeIndex = activePath == null ? 1 : GetProtocolLogIndex(activePath);
        activePath ??= GetProtocolLogPath(directory, activeIndex);
        var offset = 0;

        while (offset < bytes.Length)
        {
            var currentLength = File.Exists(activePath) ? new FileInfo(activePath).Length : 0;
            if (currentLength >= maxFileBytes)
            {
                activeIndex = activeIndex % maxFileCount + 1;
                activePath = GetProtocolLogPath(directory, activeIndex);
                File.Delete(activePath);
                currentLength = 0;
            }

            var writeCount = (int)Math.Min(bytes.Length - offset, maxFileBytes - currentLength);
            using (var stream = new FileStream(activePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, offset, writeCount);
            }

            File.SetLastWriteTimeUtc(activePath, DateTime.UtcNow);
            offset += writeCount;
        }
    }

    private static string? GetActiveProtocolLogPath(string directory, int maxFileCount)
    {
        return Enumerable.Range(1, maxFileCount)
            .Select(index => GetProtocolLogPath(directory, index))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string GetProtocolLogPath(string directory, int index)
    {
        return Path.Combine(directory, index == 1 ? "protocol-trace.txt" : $"protocol-trace-{index}.txt");
    }

    private static int GetProtocolLogIndex(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Equals("protocol-trace", StringComparison.OrdinalIgnoreCase)
            ? 1
            : int.Parse(fileName["protocol-trace-".Length..], CultureInfo.InvariantCulture);
    }

}
