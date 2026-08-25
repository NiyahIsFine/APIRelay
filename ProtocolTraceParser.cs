using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace APIRelay
{
    internal enum ProtocolTraceDirectionValue : byte
    {
        ClientToTool,
        ToolToServer,
        ServerToTool,
        ToolToClient
    }

    internal enum ProtocolTraceProtocolValue : byte
    {
        Responses,
        ChatCompletions,
        AnthropicMessages
    }

    internal readonly record struct ProtocolTraceEntry(
        DateTime Timestamp,
        uint RequestId,
        ProtocolTraceDirectionValue Direction,
        ProtocolTraceProtocolValue Protocol,
        long BodyOffset,
        long BodyLength)
    {
        public string DirectionDisplay => Direction switch
        {
            ProtocolTraceDirectionValue.ClientToTool => "client->tool",
            ProtocolTraceDirectionValue.ToolToServer => "tool->server",
            ProtocolTraceDirectionValue.ServerToTool => "server->tool",
            ProtocolTraceDirectionValue.ToolToClient => "tool->client",
            _ => Direction.ToString()
        };

        public string ProtocolDisplay => Protocol switch
        {
            ProtocolTraceProtocolValue.Responses => "Responses",
            ProtocolTraceProtocolValue.ChatCompletions => "ChatCompletions",
            ProtocolTraceProtocolValue.AnthropicMessages => "AnthropicMessages",
            _ => Protocol.ToString()
        };
    }

    internal static partial class ProtocolTraceParser
    {
        private const int ReadBufferSize = 64 * 1024;
        private const int MaxHeaderLength = 256;

        private static readonly JsonSerializerOptions IndentedJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        [GeneratedRegex(
            @"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] Request=(?<request>[0-9a-fA-F]{8}); Direction=(?<direction>ClientToTool|ToolToServer|ServerToTool|ToolToClient); Protocol=(?<protocol>Responses|ChatCompletions|AnthropicMessages)$",
            RegexOptions.CultureInvariant)]
        private static partial Regex HeaderRegex();

        public static IReadOnlyList<ProtocolTraceEntry> IndexFile(string path)
        {
            var entries = new List<ProtocolTraceEntry>();
            var readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
            Span<byte> headerBuffer = stackalloc byte[MaxHeaderLength];
            var headerLength = 0;
            var possibleHeader = true;
            long position = 0;
            long lineStart = 0;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    ReadBufferSize,
                    FileOptions.SequentialScan);

                int bytesRead;
                while ((bytesRead = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    for (var index = 0; index < bytesRead; index++)
                    {
                        var value = readBuffer[index];
                        if (value == (byte)'\n')
                        {
                            TryAddHeader(entries, headerBuffer[..headerLength], lineStart, position + 1);
                            position++;
                            lineStart = position;
                            headerLength = 0;
                            possibleHeader = true;
                            continue;
                        }

                        if (possibleHeader)
                        {
                            if ((headerLength == 0 && value != (byte)'[') || headerLength == MaxHeaderLength)
                            {
                                possibleHeader = false;
                                headerLength = 0;
                            }
                            else
                            {
                                headerBuffer[headerLength++] = value;
                            }
                        }

                        position++;
                    }
                }

                if (position > lineStart)
                {
                    TryAddHeader(entries, headerBuffer[..headerLength], lineStart, position);
                }

                FinalizeLastEntry(entries, position);
                return entries;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
            }
        }

        public static string ReadBody(string path, ProtocolTraceEntry entry)
        {
            if (entry.BodyLength <= 0)
            {
                return string.Empty;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadBufferSize,
                FileOptions.RandomAccess);

            if (entry.BodyOffset >= stream.Length)
            {
                return string.Empty;
            }

            var availableLength = Math.Min(entry.BodyLength, stream.Length - entry.BodyOffset);
            if (availableLength > int.MaxValue)
            {
                throw new IOException("The protocol trace body is too large to display.");
            }

            var bytes = ArrayPool<byte>.Shared.Rent((int)availableLength);
            try
            {
                stream.Position = entry.BodyOffset;

                var totalRead = 0;
                while (totalRead < availableLength)
                {
                    var bytesRead = stream.Read(bytes, totalRead, (int)availableLength - totalRead);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalRead += bytesRead;
                }

                while (totalRead > 0 && bytes[totalRead - 1] is (byte)'\r' or (byte)'\n')
                {
                    totalRead--;
                }

                return Encoding.UTF8.GetString(bytes, 0, totalRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        public static string FormatBody(string body)
        {
            var content = body.Trim();
            if (!TryExtractSseData(content, out var dataBlocks))
            {
                return UnescapeFirstNewlineInRuns(FormatJson(content));
            }

            var formattedBlocks = new List<string>(dataBlocks.Count);
            foreach (var block in dataBlocks)
            {
                formattedBlocks.Add(FormatJson(block));
            }

            return UnescapeFirstNewlineInRuns(string.Join(Environment.NewLine + Environment.NewLine, formattedBlocks));
        }

        private static string FormatJson(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
            }
            catch (JsonException)
            {
                // Keep non-JSON protocol markers (for example [DONE]) readable as-is.
                return json;
            }
        }

        private static void TryAddHeader(
            List<ProtocolTraceEntry> entries,
            ReadOnlySpan<byte> headerBytes,
            long headerOffset,
            long bodyOffset)
        {
            if (headerBytes.Length > 0 && headerBytes[^1] == (byte)'\r')
            {
                headerBytes = headerBytes[..^1];
            }

            if (headerBytes.IsEmpty)
            {
                return;
            }

            var header = Encoding.ASCII.GetString(headerBytes);
            var match = HeaderRegex().Match(header);
            if (!match.Success
                || !DateTime.TryParseExact(
                    match.Groups["timestamp"].ValueSpan,
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var timestamp)
                || !uint.TryParse(
                    match.Groups["request"].ValueSpan,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var requestId)
                || !TryParseDirection(match.Groups["direction"].ValueSpan, out var direction)
                || !TryParseProtocol(match.Groups["protocol"].ValueSpan, out var protocol))
            {
                return;
            }

            FinalizeLastEntry(entries, headerOffset);
            entries.Add(new ProtocolTraceEntry(timestamp, requestId, direction, protocol, bodyOffset, 0));
        }

        private static void FinalizeLastEntry(List<ProtocolTraceEntry> entries, long bodyEnd)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var lastIndex = entries.Count - 1;
            var entry = entries[lastIndex];
            entries[lastIndex] = entry with { BodyLength = Math.Max(0, bodyEnd - entry.BodyOffset) };
        }

        private static bool TryParseDirection(ReadOnlySpan<char> value, out ProtocolTraceDirectionValue direction)
        {
            if (value.SequenceEqual("ClientToTool"))
            {
                direction = ProtocolTraceDirectionValue.ClientToTool;
                return true;
            }

            if (value.SequenceEqual("ToolToServer"))
            {
                direction = ProtocolTraceDirectionValue.ToolToServer;
                return true;
            }

            if (value.SequenceEqual("ServerToTool"))
            {
                direction = ProtocolTraceDirectionValue.ServerToTool;
                return true;
            }

            if (value.SequenceEqual("ToolToClient"))
            {
                direction = ProtocolTraceDirectionValue.ToolToClient;
                return true;
            }

            direction = default;
            return false;
        }

        private static bool TryParseProtocol(ReadOnlySpan<char> value, out ProtocolTraceProtocolValue protocol)
        {
            if (value.SequenceEqual("Responses"))
            {
                protocol = ProtocolTraceProtocolValue.Responses;
                return true;
            }

            if (value.SequenceEqual("ChatCompletions"))
            {
                protocol = ProtocolTraceProtocolValue.ChatCompletions;
                return true;
            }

            if (value.SequenceEqual("AnthropicMessages"))
            {
                protocol = ProtocolTraceProtocolValue.AnthropicMessages;
                return true;
            }

            protocol = default;
            return false;
        }

        private static bool TryExtractSseData(string content, out IReadOnlyList<string> dataBlocks)
        {
            var blocks = new List<string>();
            var currentBlock = new StringBuilder();
            var hasData = false;
            using var reader = new StringReader(content);

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                {
                    if (hasData)
                    {
                        blocks.Add(currentBlock.ToString());
                        currentBlock.Clear();
                        hasData = false;
                    }

                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (hasData)
                {
                    currentBlock.Append('\n');
                }

                currentBlock.Append(line[5..].TrimStart());
                hasData = true;
            }

            if (hasData)
            {
                blocks.Add(currentBlock.ToString());
            }

            dataBlocks = blocks;
            return blocks.Count > 0;
        }

        private static string UnescapeFirstNewlineInRuns(string content)
        {
            var builder = new StringBuilder(content.Length);
            var index = 0;

            while (index < content.Length)
            {
                if (IsNewlineEscape(content, index))
                {
                    builder.Append(Environment.NewLine);
                    index += 2;

                    while (IsNewlineEscape(content, index))
                    {
                        builder.Append("\\n");
                        index += 2;
                    }

                    continue;
                }

                builder.Append(content[index]);
                index++;
            }

            return builder.ToString();
        }

        private static bool IsNewlineEscape(string content, int index)
        {
            return index + 1 < content.Length
                && content[index] == '\\'
                && content[index + 1] == 'n'
                && (index == 0 || content[index - 1] != '\\');
        }
    }
}
