using System;
using Avalonia.Threading;

namespace Notes.Services;

public sealed class AutoSaveScheduler : IAutoSaveScheduler
{
    private readonly DispatcherTimer _timer;
    private Action? _onSave;

    public event Action OnSave
    {
        add => _onSave += value;
        remove => _onSave -= value;
    }

    public AutoSaveScheduler()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    public void Bump()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        if (!_timer.IsEnabled)
        {
            return;
        }

        _timer.Stop();
        _onSave?.Invoke();
    }

    public void Cancel()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _onSave?.Invoke();
    }
}
