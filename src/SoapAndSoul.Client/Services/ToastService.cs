namespace SoapAndSoul.Client.Services;

public enum ToastKind { Ok, Deleted, Info, Error }

public sealed record Toast(Guid Id, string Text, ToastKind Kind)
{
    public string Icon => Kind switch
    {
        ToastKind.Deleted => "ph-trash",
        ToastKind.Info => "ph-info",
        ToastKind.Error => "ph-warning-circle",
        _ => "ph-check-circle",
    };

    public string CssClass => Kind switch
    {
        ToastKind.Deleted => "del",
        ToastKind.Info => "info",
        ToastKind.Error => "error",
        _ => "ok",
    };
}

/// <summary>Short auto-dismissing notifications shown at the top of the screen.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;
    public event Action? Changed;

    public void Show(string text, ToastKind kind = ToastKind.Ok)
    {
        var toast = new Toast(Guid.NewGuid(), text, kind);
        _toasts.Add(toast);
        Changed?.Invoke();
        _ = RemoveLaterAsync(toast, kind == ToastKind.Error ? 4000 : 2200);
    }

    private async Task RemoveLaterAsync(Toast toast, int delayMs)
    {
        await Task.Delay(delayMs);
        _toasts.Remove(toast);
        Changed?.Invoke();
    }
}
