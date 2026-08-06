using System.Reflection;

namespace APIRelay.Tests;

public sealed class ProtocolLogRotationTests
{
    private static readonly MethodInfo AppendRotatingProtocolLog = typeof(APIRelay.Form1).GetMethod(
        "AppendRotatingProtocolLog",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void FullLastFileCausesFirstFileToBeRecreated()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"APIRelay-{Guid.NewGuid():N}");

        try
        {
            Append(directory, "1234567890", 10, 3);
            Append(directory, "abcdefghij", 10, 3);
            Append(directory, "ABCDEFGHIJ", 10, 3);
            Append(directory, "xyz", 10, 3);

            var files = Directory.GetFiles(directory, "protocol-trace*.txt");
            Assert.Equal(3, files.Length);
            Assert.All(files, path => Assert.InRange(new FileInfo(path).Length, 1, 10));
            Assert.Equal("xyz", File.ReadAllText(Path.Combine(directory, "protocol-trace.txt")));
            Assert.Equal("abcdefghij", File.ReadAllText(Path.Combine(directory, "protocol-trace-2.txt")));
            Assert.Equal("ABCDEFGHIJ", File.ReadAllText(Path.Combine(directory, "protocol-trace-3.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void Append(string directory, string content, long maxFileBytes, int maxFileCount)
    {
        AppendRotatingProtocolLog.Invoke(null, [directory, content, maxFileBytes, maxFileCount]);
    }
}