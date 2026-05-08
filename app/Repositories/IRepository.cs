using ComposeNowPlugins.Models;

namespace ComposeNowPlugins.Repositories;

public interface IRepository<TModel, TId>
    where TModel : Model<TId>
{
    public Task<TModel?> GetAsync(TId key);
    public Task<RepositoryActionStatus> AddAsync(TModel model);
    public Task<RepositoryActionStatus> UpdateAsync(TId key, TModel model);
    public Task<RepositoryActionStatus> DeleteAsync(TId key);
}