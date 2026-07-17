using System.Reflection;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace APIRelay.Tests;

public sealed class ManagedToolLogicTests
{
    private static readonly Type FormType = typeof(APIRelay.Form1);
    private static readonly Type ProtocolType = FormType.GetNestedType("ApiRouteKind", BindingFlags.NonPublic)!;

    [Theory]
    [InlineData("Responses", "same/model:v1")]
    [InlineData("ChatCompletions", "same/model:v1")]
    [InlineData("AnthropicMessages", "claude-模型-1")]
    public void ManagedAliasRoundTrips(string protocolName, string modelId)
    {
        var protocol = Enum.Parse(ProtocolType, protocolName);
        var create = StaticMethod("CreateManagedModelAlias");
        var parse = StaticMethod("TryParseManagedModelAlias");
        var alias = Assert.IsType<string>(create.Invoke(null, [protocol, modelId]));
        object?[] arguments = [alias, null, null];

        Assert.True(Assert.IsType<bool>(parse.Invoke(null, arguments)));
        Assert.Equal(protocol, arguments[1]);
        Assert.Equal(modelId, arguments[2]);
    }

    [Fact]
    public void EqualModelIdsInDifferentProtocolsProduceDifferentAliases()
    {
        var create = StaticMethod("CreateManagedModelAlias");
        var responses = create.Invoke(null, [Enum.Parse(ProtocolType, "Responses"), "shared-model"]);
        var anthropic = create.Invoke(null, [Enum.Parse(ProtocolType, "AnthropicMessages"), "shared-model"]);

        Assert.NotEqual(responses, anthropic);
    }

    [Theory]
    [InlineData("/claude/v1/messages", "AnthropicMessages")]
    [InlineData("/codex/v1/responses", "Responses")]
    public void ManagedPathSelectsClientProtocol(string path, string expectedProtocol)
    {
        object?[] arguments = [path, null];
        var result = StaticMethod("TryResolveManagedClientProtocol").Invoke(null, arguments);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal(Enum.Parse(ProtocolType, expectedProtocol), arguments[1]);
    }

    [Fact]
    public void OrdinaryPathIsNotManaged()
    {
        object?[] arguments = ["/responses/v1/responses", null];
        Assert.False(Assert.IsType<bool>(StaticMethod("TryResolveManagedClientProtocol").Invoke(null, arguments)));
    }

    [Fact]
    public void RequestModelRewritePreservesOtherJson()
    {
        var source = Encoding.UTF8.GetBytes("{\"model\":\"alias\",\"stream\":true,\"input\":[1,2]}");
        var rewritten = Assert.IsType<byte[]>(StaticMethod("RewriteRequestModel").Invoke(null, [source, "provider-model"]));
        using var document = JsonDocument.Parse(rewritten);

        Assert.Equal("provider-model", document.RootElement.GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(2, document.RootElement.GetProperty("input").GetArrayLength());
    }

        [Fact]
        public void AnthropicRequestConvertsToResponsesContentAndTools()
        {
                var source = Encoding.UTF8.GetBytes("""
                        {
                            "model":"model-a",
                            "system":[{"type":"text","text":"system text","cache_control":{"type":"ephemeral"}}],
                            "messages":[
                                {"role":"user","content":[{"type":"text","text":"hello"}]},
                                {"role":"assistant","content":[{"type":"text","text":"calling"},{"type":"tool_use","id":"call-1","name":"lookup","input":{"key":"value"}}]},
                                {"role":"user","content":[{"type":"tool_result","tool_use_id":"call-1","content":"done"}]}
                            ],
                            "tools":[{"name":"lookup","description":"Lookup","input_schema":{"type":"object"}}]
                        }
                        """);

                var transformed = TransformRequest(source, "AnthropicMessages", "Responses");
                using var document = JsonDocument.Parse(transformed);
                var root = document.RootElement;
                var input = root.GetProperty("input");

                Assert.Equal("system text", root.GetProperty("instructions").GetString());
                Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
                Assert.Equal("output_text", input[1].GetProperty("content")[0].GetProperty("type").GetString());
                Assert.Equal("function_call", input[2].GetProperty("type").GetString());
                Assert.Equal("{\"key\":\"value\"}", input[2].GetProperty("arguments").GetString());
                Assert.Equal("function_call_output", input[3].GetProperty("type").GetString());
                Assert.Equal("done", input[3].GetProperty("output").GetString());
                Assert.Equal("function", root.GetProperty("tools")[0].GetProperty("type").GetString());
                Assert.Equal("lookup", root.GetProperty("tools")[0].GetProperty("name").GetString());
                Assert.Equal("object", root.GetProperty("tools")[0].GetProperty("parameters").GetProperty("type").GetString());
        }

            [Fact]
            public void AnthropicRequestConvertsToolMessagesToChatCompletions()
            {
                var source = Encoding.UTF8.GetBytes("""
                    {
                        "model":"model-a",
                        "messages":[
                        {"role":"assistant","content":[{"type":"text","text":"calling"},{"type":"tool_use","id":"call-1","name":"lookup","input":{"key":"value"}}]},
                        {"role":"user","content":[{"type":"tool_result","tool_use_id":"call-1","content":"done","is_error":false}]}
                        ]
                    }
                    """);

                var transformed = TransformRequest(source, "AnthropicMessages", "ChatCompletions");
                using var document = JsonDocument.Parse(transformed);
                var messages = document.RootElement.GetProperty("messages");

                Assert.Equal("assistant", messages[0].GetProperty("role").GetString());
                Assert.Equal("calling", messages[0].GetProperty("content").GetString());
                Assert.Equal("call-1", messages[0].GetProperty("tool_calls")[0].GetProperty("id").GetString());
                Assert.Equal("function", messages[0].GetProperty("tool_calls")[0].GetProperty("type").GetString());
                Assert.Equal("lookup", messages[0].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
                Assert.Equal("{\"key\":\"value\"}", messages[0].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());
                Assert.Equal("tool", messages[1].GetProperty("role").GetString());
                Assert.Equal("call-1", messages[1].GetProperty("tool_call_id").GetString());
                Assert.Equal("done", messages[1].GetProperty("content").GetString());
                Assert.DoesNotContain("tool_use", Encoding.UTF8.GetString(transformed));
                Assert.DoesNotContain("tool_result", Encoding.UTF8.GetString(transformed));
            }

    [Fact]
    public void AtomicWriteReplacesExistingBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "APIRelay.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.bin");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "old");
            StaticMethod("WriteAllBytesAtomically").Invoke(null, [path, Encoding.UTF8.GetBytes("new")]);
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
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
    public void ManagedConfigurationRestoreWritesExactBackupBytesBeforeCleanup()
    {
        var directory = CreateTemporaryDirectory();
        var target = Path.Combine(directory, "settings.json");
        var backup = Path.Combine(directory, "settings.backup");
        try
        {
            var originalBytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x7B, 0x0D, 0x0A, 0x7D };
            File.WriteAllText(target, "managed");
            File.WriteAllBytes(backup, originalBytes);
            var state = CreateApplyState(target, backup, true);

            StaticMethod("RestoreManagedConfigurationFiles").Invoke(null, [state]);

            Assert.Equal(originalBytes, File.ReadAllBytes(target));
            Assert.True(File.Exists(backup));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ManagedConfigurationRestoreRemovesOriginallyAbsentFile()
    {
        var directory = CreateTemporaryDirectory();
        var target = Path.Combine(directory, "config.toml");
        var backup = Path.Combine(directory, "config.backup");
        try
        {
            File.WriteAllText(target, "managed");
            var state = CreateApplyState(target, backup, false);
            StaticMethod("RestoreManagedConfigurationFiles").Invoke(null, [state]);
            Assert.False(File.Exists(target));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RegistrationFilteringDistinguishesEqualIdsByProtocol()
    {
        var responses = Enum.Parse(ProtocolType, "Responses");
        var anthropic = Enum.Parse(ProtocolType, "AnthropicMessages");
        var fetched = CreateTypedList("ModelListItem", CreateModelListItem("shared-model"), CreateModelListItem("other-model"));
        var registered = CreateTypedList("RegisteredModelConfig", CreateRegistration(anthropic, "shared-model"));
        var registrationFormType = FormType.GetNestedType("ModelRegistrationForm", BindingFlags.NonPublic)!;
        var method = registrationFormType.GetMethod("FilterAvailableModels", BindingFlags.Static | BindingFlags.NonPublic)!;

        var availableForResponses = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method.Invoke(null, [responses, fetched, registered]));
        var availableForAnthropic = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method.Invoke(null, [anthropic, fetched, registered]));

        Assert.Equal(2, availableForResponses.Cast<object>().Count());
        Assert.Single(availableForAnthropic.Cast<object>());
    }

    [Fact]
    public void RegistrationIsUnavailableOnlyWhenMissingFromFetchedModels()
    {
        var protocol = Enum.Parse(ProtocolType, "Responses");
        var registration = CreateRegistration(protocol, "model-a");
        var formType = FormType.GetNestedType("ModelRegistrationForm", BindingFlags.NonPublic)!;
        var method = formType.GetMethod("IsRegistrationUnavailable", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.False(Assert.IsType<bool>(method.Invoke(null, [registration, CreateTypedList("ModelListItem", CreateModelListItem("model-a"))])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [registration, CreateTypedList("ModelListItem", CreateModelListItem("model-b"))])));
    }

    [Theory]
    [InlineData("Responses", "OpenAICompatible", true)]
    [InlineData("AnthropicMessages", "Anthropic", false)]
    public void ProviderAuthenticationUsesProtocolScheme(string protocolName, string providerTypeName, bool expectsBearer)
    {
        var configType = FormType.GetNestedType("ProviderEndpointConfig", BindingFlags.NonPublic)!;
        var providerType = FormType.GetNestedType("ProviderType", BindingFlags.NonPublic)!;
        var config = Activator.CreateInstance(configType)!;
        SetProperty(config, "RouteKind", Enum.Parse(ProtocolType, protocolName));
        SetProperty(config, "ProviderType", Enum.Parse(providerType, providerTypeName));
        SetProperty(config, "AnthropicVersion", "2023-06-01");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com");

        StaticMethod("ApplyProviderAuthentication").Invoke(null, [request, config, "stored-secret"]);

        if (expectsBearer)
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "stored-secret"), request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-api-key"));
        }
        else
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("stored-secret", Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
        }
    }

    [Fact]
    public void ClaudeSettingsPreserveUnknownFieldsAndApplyManagedValues()
    {
        var settingsType = FormType.GetNestedType("ClaudeToolSettings", BindingFlags.NonPublic)!;
        var settings = Activator.CreateInstance(settingsType)!;
        SetProperty(settings, "HaikuModelAlias", "haiku-alias");
        SetProperty(settings, "SonnetModelAlias", "sonnet-alias");
        SetProperty(settings, "OpusModelAlias", "opus-alias");
        SetProperty(settings, "EnableToolSearch", true);
        SetProperty(settings, "UseMaximumEffort", true);
        var existing = "{\"autoUpdatesChannel\":\"latest\",\"env\":{\"KEEP_ME\":\"yes\"}}";
        var content = Assert.IsType<byte[]>(StaticMethod("BuildClaudeSettingsContent").Invoke(null, [existing, new Uri("http://127.0.0.1:14556/"), settings]));
        using var document = JsonDocument.Parse(content);
        var env = document.RootElement.GetProperty("env");

        Assert.Equal("latest", document.RootElement.GetProperty("autoUpdatesChannel").GetString());
        Assert.Equal("yes", env.GetProperty("KEEP_ME").GetString());
        Assert.Equal("http://127.0.0.1:14556/claude", env.GetProperty("ANTHROPIC_BASE_URL").GetString());
        Assert.Equal("APIRELAY", env.GetProperty("ANTHROPIC_AUTH_TOKEN").GetString());
        Assert.Equal("sonnet-alias", env.GetProperty("ANTHROPIC_DEFAULT_SONNET_MODEL").GetString());
        Assert.Equal("sonnet-alias", env.GetProperty("ANTHROPIC_DEFAULT_SONNET_MODEL_NAME").GetString());
        Assert.Equal("true", env.GetProperty("ENABLE_TOOL_SEARCH").GetString());
        Assert.Equal("max", env.GetProperty("CLAUDE_CODE_EFFORT_LEVEL").GetString());
    }

    [Fact]
    public void CodexSettingsPreserveUnknownValuesAndApplyManagedProvider()
    {
        var settingsType = FormType.GetNestedType("CodexToolSettings", BindingFlags.NonPublic)!;
        var settings = Activator.CreateInstance(settingsType)!;
        SetProperty(settings, "ModelAlias", "codex-alias");
        SetProperty(settings, "ReasoningEffort", "xhigh");
        var existing = "disable_response_storage = true\n[notice]\nshow = false\n";
        var content = Assert.IsType<byte[]>(StaticMethod("BuildCodexSettingsContent").Invoke(null, [existing, new Uri("http://127.0.0.1:14556/"), settings]));
        var root = TomlSerializer.Deserialize<TomlTable>(Encoding.UTF8.GetString(content))!;
        var providers = Assert.IsType<TomlTable>(root["model_providers"]);
        var relay = Assert.IsType<TomlTable>(providers["apirelay"]);

        Assert.Equal(true, root["disable_response_storage"]);
        Assert.Equal("apirelay", root["model_provider"]);
        Assert.Equal("codex-alias", root["model"]);
        Assert.Equal("xhigh", root["model_reasoning_effort"]);
        Assert.Equal("responses", relay["wire_api"]);
        Assert.Equal(false, relay["requires_openai_auth"]);
        Assert.Equal("http://127.0.0.1:14556/codex/v1", relay["base_url"]);
    }

    [Fact]
    public void ClaudeModelNameShowsOriginalModelWhenAliasIsValid()
    {
        var alias = (string)StaticMethod("CreateManagedModelAlias").Invoke(null, [Enum.Parse(ProtocolType, "AnthropicMessages"), "claude-original-7"])!;
        var settingsType = FormType.GetNestedType("ClaudeToolSettings", BindingFlags.NonPublic)!;
        var settings = Activator.CreateInstance(settingsType)!;
        SetProperty(settings, "SonnetModelAlias", alias);
        var content = Assert.IsType<byte[]>(StaticMethod("BuildClaudeSettingsContent").Invoke(null, ["{}", new Uri("http://127.0.0.1:14556/"), settings]));
        using var document = JsonDocument.Parse(content);
        var env = document.RootElement.GetProperty("env");

        Assert.Equal(alias, env.GetProperty("ANTHROPIC_DEFAULT_SONNET_MODEL").GetString());
        Assert.Equal("claude-original-7", env.GetProperty("ANTHROPIC_DEFAULT_SONNET_MODEL_NAME").GetString());
    }

    private static MethodInfo StaticMethod(string name)
    {
        return FormType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {name}");
    }

    private static MethodInfo StaticMethod(string name, params Type[] parameterTypes)
    {
        return FormType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, parameterTypes, null)
            ?? throw new InvalidOperationException($"Method not found: {name}");
    }

    private static byte[] TransformRequest(byte[] source, string fromProtocol, string toProtocol)
    {
        return Assert.IsType<byte[]>(StaticMethod("TransformRequestBody").Invoke(null,
            [source, Enum.Parse(ProtocolType, fromProtocol), Enum.Parse(ProtocolType, toProtocol)]));
    }

    [Fact]
    public void ResponsesFunctionCallStreamConvertsToAnthropicToolUse()
    {
        var stateType = FormType.GetNestedType("StreamingProtocolConversionState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, "gpt-5.6-sol")!;
        var relayRouteType = FormType.GetNestedType("RelayRoute", BindingFlags.NonPublic)!;
        var responses = Enum.Parse(ProtocolType, "Responses");
        var anthropic = Enum.Parse(ProtocolType, "AnthropicMessages");
        var relayRoute = Activator.CreateInstance(relayRouteType, responses, anthropic, false, null)!;

        var events = new[]
        {
            """{"type":"response.created","response":{"id":"resp_x","model":"gpt-5.6-sol"}}""",
            """{"type":"response.output_item.added","output_index":1,"item":{"id":"fc_x","type":"function_call","status":"in_progress","arguments":"","call_id":"call_1","name":"Skill"}}""",
            """{"type":"response.function_call_arguments.delta","item_id":"fc_x","output_index":1,"delta":"{\"args\":\"hi\",\"skill\":\"claude-api\"}"}""",
            """{"type":"response.function_call_arguments.done","item_id":"fc_x","output_index":1,"arguments":"{\"args\":\"hi\",\"skill\":\"claude-api\"}"}""",
            """{"type":"response.output_item.done","output_index":1,"item":{"id":"fc_x","type":"function_call","status":"completed","arguments":"{\"args\":\"hi\",\"skill\":\"claude-api\"}","call_id":"call_1","name":"Skill"}}""",
            """{"type":"response.completed","response":{"id":"resp_x","model":"gpt-5.6-sol","status":"completed","usage":{"input_tokens":1,"output_tokens":2,"total_tokens":3}}}"""
        };

        var output = new StringBuilder();
        var convertMethod = StaticMethod("ConvertStreamingEvent", typeof(string), relayRouteType, stateType);
        foreach (var evt in events)
        {
            output.Append(convertMethod.Invoke(null, [evt, relayRoute, state]));
        }

        output.Append(StaticMethod("BuildStreamingCompletionEvent", ProtocolType, stateType).Invoke(null, [anthropic, state]));

        var text = output.ToString();
        Assert.Contains("\"type\":\"tool_use\"", text);
        Assert.Contains("\"type\":\"input_json_delta\"", text);
        Assert.Contains("\"stop_reason\":\"tool_use\"", text);
        Assert.DoesNotContain("\"text_delta\"", text);

        // Arguments must travel inside the tool-call delta, serialized as partial_json
        Assert.Contains("skill", text);
        Assert.Contains("claude-api", text);
        Assert.Contains("input_json_delta", text);
    }

    [Fact]
    public void ResponsesFailedStreamSurfacesErrorToAnthropicClient()
    {
        var stateType = FormType.GetNestedType("StreamingProtocolConversionState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, "gpt-5.6-sol")!;
        var relayRouteType = FormType.GetNestedType("RelayRoute", BindingFlags.NonPublic)!;
        var responses = Enum.Parse(ProtocolType, "Responses");
        var anthropic = Enum.Parse(ProtocolType, "AnthropicMessages");
        var relayRoute = Activator.CreateInstance(relayRouteType, responses, anthropic, false, null)!;

        var events = new[]
        {
            """{"type":"response.created","response":{"id":"resp_x","model":"gpt-5.6-sol"}}""",
            """{"type":"response.failed","response":{"id":"resp_x","status":"failed","error":{"type":"invalid_request_error","message":"Invalid value: 'text'. Supported values are: 'input_text'..."}}}"""
        };

        var output = new StringBuilder();
        var convertMethod = StaticMethod("ConvertStreamingEvent", typeof(string), relayRouteType, stateType);
        foreach (var evt in events)
        {
            output.Append(convertMethod.Invoke(null, [evt, relayRoute, state]));
        }

        output.Append(StaticMethod("BuildStreamingCompletionEvent", ProtocolType, stateType).Invoke(null, [anthropic, state]));

        var text = output.ToString();
        Assert.Contains("Invalid value", text);
        Assert.Contains("\"text_delta\"", text);
    }

    [Fact]
    public void OpenAiToolCallStreamConvertsToAnthropicToolUse()
    {
        var stateType = FormType.GetNestedType("StreamingProtocolConversionState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, "gpt-5.6-sol")!;
        var relayRouteType = FormType.GetNestedType("RelayRoute", BindingFlags.NonPublic)!;
        var chatCompletions = Enum.Parse(ProtocolType, "ChatCompletions");
        var anthropic = Enum.Parse(ProtocolType, "AnthropicMessages");
        var relayRoute = Activator.CreateInstance(relayRouteType, chatCompletions, anthropic, false, null)!;

        var events = new[]
        {
            """{"id":"chatcmpl_x","model":"gpt-5.6-sol","choices":[{"index":0,"delta":{"role":"assistant","content":null}}]}""",
            """{"id":"chatcmpl_x","model":"gpt-5.6-sol","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"Skill","arguments":"{\"args\":"}}]}}]}""",
            """{"id":"chatcmpl_x","model":"gpt-5.6-sol","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"hi\",\"skill\":\"claude-api\"}"}}]}}]}""",
            """{"id":"chatcmpl_x","model":"gpt-5.6-sol","choices":[{"index":0,"finish_reason":"tool_calls","delta":{}}]}"""
        };

        var output = new StringBuilder();
        var convertMethod = StaticMethod("ConvertStreamingEvent", typeof(string), relayRouteType, stateType);
        foreach (var evt in events)
        {
            output.Append(convertMethod.Invoke(null, [evt, relayRoute, state]));
        }

        output.Append(StaticMethod("BuildStreamingCompletionEvent", ProtocolType, stateType).Invoke(null, [anthropic, state]));

        var text = output.ToString();
        Assert.Contains("\"type\":\"tool_use\"", text);
        Assert.Contains("\"name\":\"Skill\"", text);
        Assert.Contains("\"type\":\"input_json_delta\"", text);
        Assert.Contains("\"stop_reason\":\"tool_use\"", text);
        Assert.Contains("claude-api", text);
    }

    [Fact]
    public void ResponsesFailedJsonBodySurfacesErrorToAnthropicClient()
    {
        var source = Encoding.UTF8.GetBytes("""{"id":"resp_x","status":"failed","error":{"type":"invalid_request_error","message":"Invalid value: 'text'."}}""");
        var relayRouteTypeLocal = FormType.GetNestedType("RelayRoute", BindingFlags.NonPublic)!;
        var responses = Enum.Parse(ProtocolType, "Responses");
        var anthropic = Enum.Parse(ProtocolType, "AnthropicMessages");
        var relayRoute = Activator.CreateInstance(relayRouteTypeLocal, responses, anthropic, false, null)!;
        var convertMethod = StaticMethod("BuildClientResponse", typeof(byte[]), typeof(string), relayRouteTypeLocal, typeof(byte[]), typeof(bool));

        var clientResponseBody = convertMethod.Invoke(null, [source, "application/json", relayRoute, Array.Empty<byte>(), false])!;
        var body = clientResponseBody.GetType().GetProperty("Body")!.GetValue(clientResponseBody) as byte[] ?? Array.Empty<byte>();
        var text = Encoding.UTF8.GetString(body);

        Assert.True(text.Contains("Invalid value"), $"Expected error text in Anthropic body, got: {text}");
        Assert.Contains("\"type\":\"message\"", text);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private static object CreateApplyState(string targetPath, string backupPath, bool originallyExisted)
    {
        var stateType = FormType.GetNestedType("ManagedConfigurationApplyState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType)!;
        SetProperty(state, "TargetPath", targetPath);
        SetProperty(state, "BackupPath", backupPath);
        SetProperty(state, "TargetOriginallyExisted", originallyExisted);
        return state;
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "APIRelay.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static object CreateModelListItem(string modelId)
    {
        var type = FormType.GetNestedType("ModelListItem", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, modelId, string.Empty, null)!;
    }

    private static object CreateRegistration(object protocol, string modelId)
    {
        var type = FormType.GetNestedType("RegisteredModelConfig", BindingFlags.NonPublic)!;
        var registration = Activator.CreateInstance(type)!;
        SetProperty(registration, "Protocol", protocol);
        SetProperty(registration, "ModelId", modelId);
        return registration;
    }

    private static object CreateTypedList(string nestedTypeName, params object[] values)
    {
        var elementType = FormType.GetNestedType(nestedTypeName, BindingFlags.NonPublic)!;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var value in values)
        {
            list.Add(value);
        }
        return list;
    }
}