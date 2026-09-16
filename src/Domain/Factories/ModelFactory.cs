using ComposeNowPlugins.Domain.Models;

namespace ComposeNowPlugins.Domain.Factories;

public abstract class ModelFactory<TModel, TId>
    where TModel : Model<TId>
{
    protected TModel Create(Func<TId, TModel> createModel)
    {
        return createModel(CreateNewId());
    }

    protected abstract TId CreateNewId();
}
