using System;

namespace LocalFileAgent.Application.Search;

public interface IDispatcherService
{
    void Invoke(Action action);
}

public sealed class ImmediateDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
