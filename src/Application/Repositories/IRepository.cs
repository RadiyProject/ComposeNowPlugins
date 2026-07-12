using ComposeNowPlugins.Domain.Models;

namespace ComposeNowPlugins.Application.Repositories;

public interface IRepository<TModel, TId>
    where TModel : Model<TId>
{
    public Task<TModel?> GetAsync(TId key);
    public Task AddAsync(TModel model);
    public Task UpdateAsync(TId key, TModel model);
    public Task DeleteAsync(TId key);
}
