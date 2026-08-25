namespace APIRelay.Tests;

public sealed class ProtocolTraceParserTests
{
    [Fact]
    public void IndexFileReturnsEveryLoggedDirectionAsSeparateEntry()
    {
        const string content = """
            [2026-08-25 10:20:30.001] Request=abcdef12; Direction=ClientToTool; Protocol=Responses
            {"model":"example","input":"hello"}

            [2026-08-25 10:20:30.002] Request=abcdef12; Direction=ToolToServer; Protocol=AnthropicMessages
            {"model":"example","messages":[]}

            [2026-08-25 10:20:31.003] Request=abcdef12; Direction=ServerToTool; Protocol=AnthropicMessages
            {"type":"content_block_delta"}

            [2026-08-25 10:20:31.004] Request=abcdef12; Direction=ToolToClient; Protocol=Responses
            {"type":"response.output_text.delta"}
            """;
        var path = WriteTemporaryTrace(content);

        try
        {
            var entries = ProtocolTraceParser.IndexFile(path);

            Assert.Collection(
                entries,
                entry => Assert.Equal("client->tool", entry.DirectionDisplay),
                entry => Assert.Equal("tool->server", entry.DirectionDisplay),
                entry => Assert.Equal("server->tool", entry.DirectionDisplay),
                entry => Assert.Equal("tool->client", entry.DirectionDisplay));
            Assert.All(entries, entry => Assert.Equal(0xabcdef12U, entry.RequestId));
            Assert.Equal("{\"model\":\"example\",\"input\":\"hello\"}", ProtocolTraceParser.ReadBody(path, entries[0]));
            Assert.Equal("{\"type\":\"response.output_text.delta\"}", ProtocolTraceParser.ReadBody(path, entries[3]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IndexFileUsesByteOffsetsForUtf8Bodies()
    {
        const string content = """
            [2026-08-25 10:20:30.001] Request=abcdef12; Direction=ServerToTool; Protocol=Responses
            {"text":"你好"}

            [2026-08-25 10:20:30.002] Request=abcdef12; Direction=ServerToTool; Protocol=Responses
            {"type":"response.output_text.delta","delta":"世界"}
            """;
        var path = WriteTemporaryTrace(content);

        try
        {
            var entries = ProtocolTraceParser.IndexFile(path);

            Assert.Equal(2, entries.Count);
            Assert.Equal("{\"text\":\"你好\"}", ProtocolTraceParser.ReadBody(path, entries[0]));
            Assert.Equal("{\"type\":\"response.output_text.delta\",\"delta\":\"世界\"}", ProtocolTraceParser.ReadBody(path, entries[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GroupEntriesMergesMatchingRequestIdsAndPreservesMessageOrder()
    {
        var firstTime = new DateTime(2026, 8, 25, 10, 20, 30, 1);
        var entries = new[]
        {
            CreateEntry(firstTime, 0xabcdef12, ProtocolTraceDirectionValue.ClientToTool),
            CreateEntry(firstTime.AddMilliseconds(1), 0x12345678, ProtocolTraceDirectionValue.ClientToTool),
            CreateEntry(firstTime.AddMilliseconds(2), 0xabcdef12, ProtocolTraceDirectionValue.ToolToServer),
            CreateEntry(firstTime.AddMilliseconds(3), 0xabcdef12, ProtocolTraceDirectionValue.ServerToTool),
            CreateEntry(firstTime.AddMilliseconds(4), 0x12345678, ProtocolTraceDirectionValue.ToolToClient)
        };

        var groups = ProtocolTraceViewerForm.GroupEntries(entries);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal(0xabcdef12U, group.RequestId);
                Assert.Equal(
                    [
                        ProtocolTraceDirectionValue.ClientToTool,
                        ProtocolTraceDirectionValue.ToolToServer,
                        ProtocolTraceDirectionValue.ServerToTool
                    ],
                    group.Entries.Select(entry => entry.Direction));
            },
            group =>
            {
                Assert.Equal(0x12345678U, group.RequestId);
                Assert.Equal(
                    [ProtocolTraceDirectionValue.ClientToTool, ProtocolTraceDirectionValue.ToolToClient],
                    group.Entries.Select(entry => entry.Direction));
            });
    }

    [Fact]
    public void FormatBodyIndentsJsonAndUnescapesOnlyFirstNewlineInRun()
    {
        const string body = "{\"message\":\"first\\n\\nsecond\",\"nested\":{\"value\":1}}";

        var formatted = ProtocolTraceParser.FormatBody(body);

        Assert.Contains(Environment.NewLine + "  \"message\"", formatted);
        Assert.Contains("first" + Environment.NewLine + "\\nsecond", formatted);
        Assert.Contains(Environment.NewLine + "  \"nested\": {" + Environment.NewLine, formatted);
    }

    [Fact]
    public void EnumerateLogFilesReturnsAllExistingLogsNewestFirst()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"APIRelay-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(directory);
            var firstPath = Path.Combine(directory, "protocol-trace.txt");
            var thirdPath = Path.Combine(directory, "protocol-trace-3.txt");
            File.WriteAllText(firstPath, "first");
            File.WriteAllText(thirdPath, "third");
            File.SetLastWriteTimeUtc(firstPath, new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(thirdPath, new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc));

            var files = ProtocolTraceViewerForm.EnumerateLogFiles(directory, 20);

            Assert.Equal([thirdPath, firstPath], files);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void FormatBodyExtractsSseDataBeforeFormattingJson()
    {
        const string body = "event: message\ndata: {\"type\":\"delta\",\"text\":\"hello\"}";

        var formatted = ProtocolTraceParser.FormatBody(body);

        Assert.DoesNotContain("data:", formatted);
        Assert.Contains("\"type\": \"delta\"", formatted);
    }

    [Fact]
    public void FormatBodyFormatsEachSseEventIndividually()
    {
        const string body = """
            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

            event: message_stop
            data: {"type":"message_stop"}
            """;

        var formatted = ProtocolTraceParser.FormatBody(body);

        Assert.DoesNotContain("data:", formatted);
        Assert.DoesNotContain("event:", formatted);
        Assert.Contains("\"type\": \"content_block_stop\"", formatted);
        Assert.Contains("\"stop_reason\": \"end_turn\"", formatted);
        Assert.Contains("\"type\": \"message_stop\"", formatted);
        Assert.Contains(Environment.NewLine + Environment.NewLine, formatted);
    }

    private static ProtocolTraceEntry CreateEntry(
        DateTime timestamp,
        uint requestId,
        ProtocolTraceDirectionValue direction)
    {
        return new ProtocolTraceEntry(
            timestamp,
            requestId,
            direction,
            ProtocolTraceProtocolValue.Responses,
            0,
            0);
    }

    private static string WriteTemporaryTrace(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"APIRelay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }
}
