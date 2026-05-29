using System;

namespace Notes.Services;

public interface IAutoSaveScheduler
{
    event Action OnSave;
    void Bump();
    void Flush();
    void Cancel();
}
