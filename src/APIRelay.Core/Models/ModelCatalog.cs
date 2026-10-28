using static APIRelay.RelayEngine;

namespace APIRelay;

internal static class ModelCatalog
{
    internal static List<ModelListItem> FilterAvailableModels(ApiRouteKind protocol,
        IEnumerable<ModelListItem> fetched, IEnumerable<RegisteredModelConfig> registered)
    {
        var ids = registered.Where(model => model.Protocol == protocol).Select(model => model.ModelId).ToHashSet(StringComparer.Ordinal);
        return fetched.Where(model => !ids.Contains(model.Id)).ToList();
    }
}
