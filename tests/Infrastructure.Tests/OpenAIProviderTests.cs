using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// FR-7, AD-11, AD-16 — the two real providers, driven over the wire against a loopback
/// <see cref="HttpListener"/> stub. No test here reaches a real model server or the internet.
/// </summary>
/// <remarks>
/// The stub answers <c>/models</c> and <c>/chat/completions</c> the way LM Studio, Ollama and Azure
/// OpenAI's v1 endpoint do, and records every request, so the assertions are about what the real
/// OpenAI SDK and <c>Microsoft.Extensions.AI.OpenAI</c> adapter actually put on the wire.
/// </remarks>
public sealed class OpenAIProviderTests
{
    private const string LocalModel = "qwen2.5-7b-instruct";

    private static readonly FixtureCatalog Catalog = new();

    // --- LocalOpenAI: the happy path, end to end through the unchanged extractor ---------------

    /// <summary>
    /// A fixture case's committed answer, served as the chat completion's content, comes back
    /// Succeeded with the same proposals — and the request carries the model, a json_schema
    /// response format, and <c>strict: true</c> (the key the extractor's remark asked this story to
    /// re-confirm against the pinned adapter).
    /// </summary>
    [Fact]
    public async Task A_local_server_that_lists_the_model_answers_a_fixture_case_through_the_extractor()
    {
        FixtureCase fixture = Case("office-move-planning");

        await using OpenAIStub stub = new() { ModelIds = ["other-model", LocalModel], ChatContent = fixture.ExpectedJson };

        AiSettings settings = Settings(LocalOpenAIChatClientFactory.ProviderName, callTimeoutSeconds: 30);
        LocalOpenAIChatClientFactory factory = new(settings, new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        await factory.VerifyAsync(TestContext.Current.CancellationToken);

        ExtractionResult result = await Extractor(factory, settings).ExtractAsync(
            new ExtractionRequest(fixture.Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);

        ExtractionOutput expected = JsonSerializer.Deserialize<ExtractionOutput>(fixture.ExpectedJson, ExtractionOutput.SerializerOptions)!;

        Assert.NotEmpty(expected.Actions);
        Assert.Empty(result.Dropped);
        Assert.Equal(expected.Actions.Select(action => action.SourceExcerpt), result.Kept.Select(proposal => proposal.SourceExcerpt));
        Assert.Equal(expected.Actions.Select(action => action.Description), result.Kept.Select(proposal => proposal.Description));
        Assert.Equal(LocalOpenAIChatClientFactory.ProviderName, result.Metrics.Provider);
        Assert.Equal(LocalModel, result.Metrics.Model);
        Assert.Equal(10, result.Metrics.InputTokens);
        Assert.Equal(5, result.Metrics.OutputTokens);

        CapturedRequest chat = Assert.Single(stub.ChatRequests);
        JsonNode body = JsonNode.Parse(chat.Body)!;

        Assert.Equal("/v1/chat/completions", chat.Path);
        Assert.Equal(LocalModel, (string?)body["model"]);
        Assert.Equal("json_schema", (string?)body["response_format"]!["type"]);
        Assert.True((bool?)body["response_format"]!["json_schema"]!["strict"], chat.Body);
        Assert.Equal($"Bearer {LocalOpenAIChatClientFactory.PlaceholderApiKey}", chat.Authorization);
    }

    /// <summary>The probe asks the same base URL the chat calls go to, with or without a trailing slash.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task The_probe_asks_the_models_endpoint_under_the_base_url(string trailing)
    {
        await using OpenAIStub stub = new() { ModelIds = [LocalModel] };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl.TrimEnd('/') + trailing, LocalModel));

        await factory.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/v1/models", Assert.Single(stub.Requests).Path);
    }

    /// <summary>A server that lists nothing is not proof the model is missing; some builds load lazily.</summary>
    [Fact]
    public async Task A_server_that_lists_no_models_passes_the_probe()
    {
        await using OpenAIStub stub = new() { ModelIds = [] };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        await factory.VerifyAsync(TestContext.Current.CancellationToken);
    }

    // --- LocalOpenAI: the probe's refusals ----------------------------------------------------

    [Fact]
    public async Task An_unreachable_local_server_fails_the_probe_naming_the_provider_and_the_models_url()
    {
        string baseUrl = $"http://127.0.0.1:{OpenAIStub.ClosedPort()}/v1";

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(baseUrl, LocalModel));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Ai:Provider", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("LocalOpenAI", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"{baseUrl}/models", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalOpenAIChatClientFactory.PlaceholderApiKey, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_does_not_list_the_model_fails_the_probe_listing_what_it_has()
    {
        await using OpenAIStub stub = new() { ModelIds = ["other-model"] };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("LocalOpenAI", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(LocalModel, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("other-model", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_list_in_the_message_is_capped_at_twenty()
    {
        await using OpenAIStub stub = new() { ModelIds = [.. Enumerable.Range(1, 25).Select(index => $"model-{index:00}")] };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("model-20", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("model-21", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("5 more", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_models_endpoint_that_answers_an_error_fails_the_probe_with_the_status()
    {
        await using OpenAIStub stub = new() { ModelsStatus = 500 };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("LocalOpenAI", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("500", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("/models", thrown.Message, StringComparison.Ordinal);

        // No SDK retry: the probe is one request.
        Assert.Single(stub.Requests);
    }

    // --- LocalOpenAI: the per-call budget and the retry count ---------------------------------

    /// <summary>
    /// NFR-1, AD-11, DW-17 — a model slower than <c>Ai:CallTimeoutSeconds</c> is two timed-out
    /// attempts and a Failed run, never a hidden SDK retry. The extractor's budget fires before the
    /// SDK's own timeout, so the reason is the extractor's timeout wording.
    /// </summary>
    [Fact]
    public async Task A_slow_model_fails_the_run_after_exactly_two_calls_inside_the_budget()
    {
        await using OpenAIStub stub = new() { ModelIds = [LocalModel], ChatDelay = TimeSpan.FromSeconds(30) };

        AiSettings settings = Settings(LocalOpenAIChatClientFactory.ProviderName, callTimeoutSeconds: 1);
        LocalOpenAIChatClientFactory factory = new(settings, new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        Stopwatch elapsed = Stopwatch.StartNew();

        ExtractionResult result = await Extractor(factory, settings).ExtractAsync(
            new ExtractionRequest(Case("office-move-planning").Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        elapsed.Stop();

        Assert.False(result.IsSucceeded);
        Assert.Contains("did not answer within Ai:CallTimeoutSeconds (1s) on attempt 2 of 2", result.FailureReason!, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"Two 1-second attempts took {elapsed.ElapsedMilliseconds} ms.");
        Assert.Equal(2, stub.ChatRequests.Count);
    }

    /// <summary>
    /// AD-11 — no SDK retry on the chat path either: a 500 from <c>/chat/completions</c> is one
    /// failed extractor attempt, so the run is Failed after exactly two HTTP calls, not eight.
    /// </summary>
    [Fact]
    public async Task A_chat_endpoint_that_answers_an_error_is_one_call_per_attempt()
    {
        await using OpenAIStub stub = new() { ModelIds = [LocalModel], ChatStatus = 500 };

        AiSettings settings = Settings(LocalOpenAIChatClientFactory.ProviderName, callTimeoutSeconds: 30);
        LocalOpenAIChatClientFactory factory = new(settings, new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        ExtractionResult result = await Extractor(factory, settings).ExtractAsync(
            new ExtractionRequest(Case("office-move-planning").Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSucceeded);
        Assert.Contains("attempt 2 of 2", result.FailureReason!, StringComparison.Ordinal);
        Assert.Equal(2, stub.ChatRequests.Count);
    }

    /// <summary>
    /// DW-17 — the SDK's own timeout sits a positive margin above the per-call budget, so the
    /// extractor's budget token always fires first. Shrinking or dropping the margin fails here.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    public void The_sdk_network_timeout_sits_above_the_per_call_budget(int callTimeoutSeconds)
    {
        AiSettings settings = Settings(LocalOpenAIChatClientFactory.ProviderName, callTimeoutSeconds);

        TimeSpan timeout = OpenAIWire.ChatNetworkTimeout(settings);

        Assert.True(OpenAIWire.NetworkTimeoutMargin > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(settings.CallTimeoutSeconds) + OpenAIWire.NetworkTimeoutMargin, timeout);
        Assert.Equal(timeout, OpenAIWire.Options(new Uri("http://127.0.0.1:1/v1/"), timeout).NetworkTimeout);
    }

    /// <summary>AD-16 — a <c>/models</c> that hangs is refused after the fixed ten-second probe timeout.</summary>
    [Fact]
    public async Task A_models_endpoint_that_hangs_fails_the_probe_after_the_probe_timeout()
    {
        await using OpenAIStub stub = new() { ModelIds = [LocalModel], ModelsDelay = TimeSpan.FromSeconds(60) };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        Stopwatch elapsed = Stopwatch.StartNew();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        elapsed.Stop();

        Assert.Contains("did not answer within 10 seconds", thrown.Message, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"The probe took {elapsed.ElapsedMilliseconds} ms.");
    }

    /// <summary>A host shutting down mid-probe sees its own cancellation, not a provider failure.</summary>
    [Fact]
    public async Task A_caller_cancellation_during_the_probe_is_rethrown_as_cancellation()
    {
        await using OpenAIStub stub = new() { ModelIds = [LocalModel], ModelsDelay = TimeSpan.FromSeconds(60) };

        LocalOpenAIChatClientFactory factory = new(
            Settings(LocalOpenAIChatClientFactory.ProviderName),
            new LocalOpenAISettings(stub.BaseUrl, LocalModel));

        using CancellationTokenSource shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        shutdown.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.VerifyAsync(shutdown.Token));
    }

    // --- AzureOpenAI ---------------------------------------------------------------------------

    /// <summary>Credential presence only: a complete section passes with no network at all.</summary>
    [Fact]
    public async Task A_complete_azure_section_passes_the_probe_without_the_network()
    {
        AzureOpenAIChatClientFactory factory = new(
            Settings(AzureOpenAIChatClientFactory.ProviderName),
            new AzureOpenAISettings("https://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", "not-a-real-key"));

        await factory.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Equal("AzureOpenAI", factory.Provider);
        Assert.Equal("gpt-4o-mini", factory.Model);
        Assert.Equal("example-resource.openai.azure.com", factory.EndpointHost);
    }

    [Theory]
    [InlineData("Ai:AzureOpenAI:Endpoint", "", "gpt-4o-mini", "not-a-real-key")]
    [InlineData("Ai:AzureOpenAI:Model", "https://example-resource.openai.azure.com/openai/v1/", " ", "not-a-real-key")]
    [InlineData("Ai:AzureOpenAI:ApiKey", "https://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", "")]
    [InlineData("Ai:AzureOpenAI:Endpoint", "http://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", "not-a-real-key")]
    [InlineData("Ai:AzureOpenAI:Endpoint", "not-a-url", "gpt-4o-mini", "not-a-real-key")]
    public async Task An_incomplete_azure_section_fails_the_probe_naming_the_key(string key, string endpoint, string model, string apiKey)
    {
        AzureOpenAIChatClientFactory factory = new(
            Settings(AzureOpenAIChatClientFactory.ProviderName),
            new AzureOpenAISettings(endpoint, model, apiKey));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.VerifyAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Ai:Provider is AzureOpenAI", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(key, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-key", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>The key is sent as the SDK's bearer credential, to the configured endpoint's chat route.</summary>
    [Fact]
    public async Task An_azure_call_sends_the_api_key_as_the_bearer_credential()
    {
        FixtureCase fixture = Case("office-move-planning");

        await using OpenAIStub stub = new() { ChatContent = fixture.ExpectedJson };

        AiSettings settings = Settings(AzureOpenAIChatClientFactory.ProviderName, callTimeoutSeconds: 30);
        AzureOpenAIChatClientFactory factory = new(settings, new AzureOpenAISettings(stub.BaseUrl, "gpt-4o-mini", "not-a-real-key"));

        ExtractionResult result = await Extractor(factory, settings).ExtractAsync(
            new ExtractionRequest(fixture.Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);

        CapturedRequest chat = Assert.Single(stub.ChatRequests);

        Assert.Equal("/v1/chat/completions", chat.Path);
        Assert.Equal("Bearer not-a-real-key", chat.Authorization);
        Assert.Equal("gpt-4o-mini", (string?)JsonNode.Parse(chat.Body)!["model"]);
    }

    [Fact]
    public void The_azure_settings_never_print_the_api_key()
    {
        AzureOpenAISettings settings = new("https://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", "not-a-real-key");

        string printed = settings.ToString();

        Assert.DoesNotContain("not-a-real-key", printed, StringComparison.Ordinal);
        Assert.Contains("gpt-4o-mini", printed, StringComparison.Ordinal);
        Assert.Contains("<redacted>", printed, StringComparison.Ordinal);
    }

    // --- Both ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>ChatClientFactories.Registered</c> builds every factory to list them, so a factory with
    /// an empty sub-section (the Fake is active) must construct and report without throwing.
    /// </summary>
    [Fact]
    public void Both_factories_construct_with_an_empty_section()
    {
        AiSettings settings = Settings(FakeChatClientFactory.ProviderName);

        IChatClientFactory local = new LocalOpenAIChatClientFactory(settings, new LocalOpenAISettings(string.Empty, string.Empty));
        IChatClientFactory azure = new AzureOpenAIChatClientFactory(settings, new AzureOpenAISettings(string.Empty, string.Empty, string.Empty));

        Assert.Equal("LocalOpenAI", local.Provider);
        Assert.Equal("AzureOpenAI", azure.Provider);
        Assert.Null(local.EndpointHost);
        Assert.Null(azure.EndpointHost);
    }

    /// <summary>The Fake has nothing to probe.</summary>
    [Fact]
    public async Task The_fake_probe_is_a_no_op()
    {
        await new FakeChatClientFactory(Catalog).VerifyAsync(TestContext.Current.CancellationToken);
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static FixtureCase Case(string stem) =>
        Catalog.Cases.Single(fixture => fixture.Stem == stem);

    private static AiSettings Settings(string provider, int callTimeoutSeconds = 90) =>
        new(provider, "v1", callTimeoutSeconds, 0.70);

    private static IActionExtractor Extractor(IChatClientFactory factory, AiSettings settings) =>
        new ChatClientActionExtractor(factory.Create(), new PromptCatalog(settings), factory, settings, TimeProvider.System);

    /// <summary>One request the stub received.</summary>
    private sealed record CapturedRequest(string Method, string Path, string? Authorization, string Body);

    /// <summary>
    /// A loopback OpenAI-compatible server: <c>GET /v1/models</c> and
    /// <c>POST /v1/chat/completions</c>, on a free port, recording every request.
    /// </summary>
    private sealed class OpenAIStub : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _loop;
        private readonly ConcurrentQueue<CapturedRequest> _requests = new();

        public OpenAIStub()
        {
            int port = FreePort();

            BaseUrl = $"http://127.0.0.1:{port}/v1/";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        /// <summary>The base URL to configure, trailing slash included.</summary>
        public string BaseUrl { get; }

        public string[] ModelIds { get; init; } = [];

        public int ModelsStatus { get; init; } = 200;

        public string ChatContent { get; init; } = """{"actions":[]}""";

        public TimeSpan ChatDelay { get; init; } = TimeSpan.Zero;

        public int ChatStatus { get; init; } = 200;

        public TimeSpan ModelsDelay { get; init; } = TimeSpan.Zero;

        public IReadOnlyList<CapturedRequest> Requests => [.. _requests];

        public IReadOnlyList<CapturedRequest> ChatRequests =>
            [.. _requests.Where(request => request.Path.EndsWith("/chat/completions", StringComparison.Ordinal))];

        /// <summary>A loopback port nothing is listening on.</summary>
        public static int ClosedPort() => FreePort();

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            _listener.Close();

            try
            {
                await _loop;
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                // The listener was closed under the pending accept, which is how it stops.
            }

            _stopping.Dispose();
        }

        private static int FreePort()
        {
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                string path = context.Request.Url!.AbsolutePath;

                _requests.Enqueue(new CapturedRequest(context.Request.HttpMethod, path, context.Request.Headers["Authorization"], body));

                if (path.EndsWith("/models", StringComparison.Ordinal))
                {
                    if (ModelsDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ModelsDelay, _stopping.Token);
                    }

                    await RespondAsync(context, ModelsStatus, ModelsStatus == 200 ? ModelsBody() : """{"error":{"message":"boom"}}""");
                }
                else if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
                {
                    if (ChatDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ChatDelay, _stopping.Token);
                    }

                    await RespondAsync(context, ChatStatus, ChatStatus == 200 ? ChatBody() : """{"error":{"message":"boom"}}""");
                }
                else
                {
                    await RespondAsync(context, 404, """{"error":{"message":"not found"}}""");
                }
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException or IOException)
            {
                // The client gave up (a timed-out attempt) or the stub is stopping.
            }
        }

        private string ModelsBody() =>
            new JsonObject
            {
                ["object"] = "list",
                ["data"] = new JsonArray(
                [
                    .. ModelIds.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["object"] = "model", ["owned_by"] = "organization_owner" }),
                ]),
            }.ToJsonString();

        private string ChatBody() =>
            new JsonObject
            {
                ["id"] = "chatcmpl-stub",
                ["object"] = "chat.completion",
                ["created"] = 1_760_000_000,
                ["model"] = "stub",
                ["choices"] = new JsonArray(
                    new JsonObject
                    {
                        ["index"] = 0,
                        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = ChatContent },
                        ["finish_reason"] = "stop",
                    }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 5, ["total_tokens"] = 15 },
            }.ToJsonString();

        private static async Task RespondAsync(HttpListenerContext context, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;

            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
