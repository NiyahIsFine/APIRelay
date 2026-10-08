using System.Text.Json;

namespace APIRelay
{
    internal readonly record struct ModelsDevPrice(
        string ModelName,
        decimal InputCostPerMillion,
        decimal OutputCostPerMillion,
        decimal? CacheHitCostPerMillion,
        decimal? CacheCreationCostPerMillion);

    internal static class ModelsDevPricing
    {
        public const string ApiUrl = "https://models.dev/api.json";

        // Providers that publish their own models. When a model id is listed by one of these,
        // its price wins over resellers and gateways that list the same id.
        private static readonly string[] OfficialProviders =
        {
            "anthropic",
            "openai",
            "google",
            "deepseek",
            "xai",
            "mistral",
            "cohere",
            "moonshotai",
            "moonshotai-cn",
            "zhipuai",
            "zai",
            "alibaba",
            "minimax",
            "minimax-cn",
            "xiaomi",
            "stepfun",
            "stepfun-ai",
            "volcengine",
            "meta",
            "llama",
            "perplexity",
            "ai21",
            "inception",
            "upstage",
            "sarvam",
            "longcat"
        };

        public static async Task<List<ModelsDevPrice>> FetchAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(document.RootElement);
        }

        public static List<ModelsDevPrice> Parse(JsonElement root)
        {
            var candidatesByModel = new Dictionary<string, List<(string Provider, ModelsDevPrice Price)>>(StringComparer.OrdinalIgnoreCase);
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new List<ModelsDevPrice>();
            }

            foreach (var provider in root.EnumerateObject())
            {
                if (provider.Value.ValueKind != JsonValueKind.Object
                    || !provider.Value.TryGetProperty("models", out var models)
                    || models.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var model in models.EnumerateObject())
                {
                    var modelName = model.Name.Trim();

                    // Prefixed ids such as "anthropic/claude-sonnet-4" are gateway-specific aliases.
                    if (string.IsNullOrWhiteSpace(modelName) || modelName.Contains('/')
                        || !TryReadPrice(modelName, model.Value, out var price))
                    {
                        continue;
                    }

                    if (!candidatesByModel.TryGetValue(modelName, out var candidates))
                    {
                        candidates = new List<(string, ModelsDevPrice)>();
                        candidatesByModel[modelName] = candidates;
                    }

                    candidates.Add((provider.Name, price));
                }
            }

            return candidatesByModel.Values
                .Select(SelectPrice)
                .OrderBy(price => price.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ModelsDevPrice SelectPrice(List<(string Provider, ModelsDevPrice Price)> candidates)
        {
            foreach (var officialProvider in OfficialProviders)
            {
                foreach (var candidate in candidates)
                {
                    if (string.Equals(candidate.Provider, officialProvider, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate.Price;
                    }
                }
            }

            // Otherwise take the price most providers agree on; ties keep the first listed.
            return candidates
                .Select((candidate, index) => (candidate.Price, Index: index))
                .GroupBy(candidate => (
                    candidate.Price.InputCostPerMillion,
                    candidate.Price.OutputCostPerMillion,
                    candidate.Price.CacheHitCostPerMillion,
                    candidate.Price.CacheCreationCostPerMillion))
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Min(candidate => candidate.Index))
                .First()
                .First()
                .Price;
        }

        private static bool TryReadPrice(string modelName, JsonElement model, out ModelsDevPrice price)
        {
            price = default;
            if (model.ValueKind != JsonValueKind.Object
                || !model.TryGetProperty("cost", out var cost)
                || cost.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var input = ReadCost(cost, "input");
            var output = ReadCost(cost, "output");

            // Free or unpriced listings would wipe out real prices, so skip them.
            if (input is not > 0m && output is not > 0m)
            {
                return false;
            }

            price = new ModelsDevPrice(
                modelName,
                RoundCost(input ?? 0m),
                RoundCost(output ?? 0m),
                ReadCost(cost, "cache_read") is { } cacheRead ? RoundCost(cacheRead) : null,
                ReadCost(cost, "cache_write") is { } cacheWrite ? RoundCost(cacheWrite) : null);
            return true;
        }

        private static decimal? ReadCost(JsonElement cost, string propertyName)
        {
            if (!cost.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDecimal(out var result)
                || result < 0m)
            {
                return null;
            }

            return result;
        }

        // Source data contains float artifacts such as 0.12500000000000003.
        private static decimal RoundCost(decimal value)
        {
            return Math.Round(value, 8, MidpointRounding.AwayFromZero) / 1.000000000000000000000000000000000m;
        }
    }
}
