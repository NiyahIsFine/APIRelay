using System.Text.Json;

namespace APIRelay.Tests;

public sealed class ModelsDevPricingTests
{
    [Fact]
    public void OfficialProviderPriceWinsOverResellers()
    {
        var prices = Parse("""
            {
              "venice": { "models": { "claude-sonnet-4-5": { "cost": { "input": 3.75, "output": 18.75, "cache_read": 0.375, "cache_write": 4.69 } } } },
              "aihubmix": { "models": { "claude-sonnet-4-5": { "cost": { "input": 3.75, "output": 18.75 } } } },
              "anthropic": { "models": { "claude-sonnet-4-5": { "cost": { "input": 3, "output": 15, "cache_read": 0.3, "cache_write": 3.75 } } } }
            }
            """);

        var price = Assert.Single(prices);
        Assert.Equal(new ModelsDevPrice("claude-sonnet-4-5", 3m, 15m, 0.3m, 3.75m), price);
    }

    [Fact]
    public void MostCommonPriceWinsWithoutOfficialProvider()
    {
        var prices = Parse("""
            {
              "a": { "models": { "some-model": { "cost": { "input": 9, "output": 9 } } } },
              "b": { "models": { "some-model": { "cost": { "input": 1, "output": 2 } } } },
              "c": { "models": { "some-model": { "cost": { "input": 1, "output": 2 } } } }
            }
            """);

        var price = Assert.Single(prices);
        Assert.Equal(new ModelsDevPrice("some-model", 1m, 2m, null, null), price);
    }

    [Fact]
    public void SkipsPrefixedFreeAndUnpricedModels()
    {
        var prices = Parse("""
            {
              "openrouter": { "models": { "openai/gpt-5": { "cost": { "input": 1.25, "output": 10 } } } },
              "iflowcn": { "models": { "glm-4.6": { "cost": { "input": 0, "output": 0 } } } },
              "snowflake": { "models": { "no-cost": { "name": "No cost" } } },
              "broken": { "name": "No models" }
            }
            """);

        Assert.Empty(prices);
    }

    [Fact]
    public void RoundsFloatArtifacts()
    {
        var prices = Parse("""
            { "helicone": { "models": { "gpt-5": { "cost": { "input": 1.25, "output": 10, "cache_read": 0.12500000000000003 } } } } }
            """);

        var price = Assert.Single(prices);
        Assert.Equal("0.125", price.CacheHitCostPerMillion?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(price.CacheCreationCostPerMillion);
    }

    private static List<ModelsDevPrice> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ModelsDevPricing.Parse(document.RootElement);
    }
}
