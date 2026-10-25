namespace PolyView;

public sealed partial class ModelViewport
{
    public ModelAnimationSource? GetAnimationSource()
    {
        var model = TryGetObject(_selectedId, out var selected) ? selected : _objects.LastOrDefault(o => o.SourcePath != null);
        var path = model?.AnimationSourcePath ?? model?.SourcePath;
        return model == null || path == null ? null : new ModelAnimationSource(model.Id, model.Name, path);
    }
}
