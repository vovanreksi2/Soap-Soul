using SoapAndSoul.Client.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Client.State;

public enum SaveStatus { Saved, Pending, Saving, Invalid, Failed }

/// <summary>
/// Working copy of one recipe with auto-save: every change is saved about a second after the
/// last edit, and immediately when the screen closes or the app goes to the background.
/// </summary>
public sealed class RecipeEditor(RecipeDto recipe, CatalogState catalog, ToastService toasts) : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(900);

    private CancellationTokenSource? _debounce;
    private Task? _saving;
    private int _changeSeq;
    private int _savedSeq;

    public RecipeDto Draft { get; private set; } = recipe;
    public SaveStatus Status { get; private set; } = SaveStatus.Saved;
    public string? StatusMessage { get; private set; }

    /// <summary>True once anything was saved; the list shows "Рецепт оновлено" on return.</summary>
    public bool SavedAnything { get; private set; }

    public bool IsDirty => _changeSeq != _savedSeq;

    public CosmeticLine Line => Draft.Line;

    public event Action? Changed;

    public void Update(Func<RecipeDto, RecipeDto> change)
    {
        Draft = change(Draft);
        _changeSeq++;
        Status = SaveStatus.Pending;
        StatusMessage = null;
        Changed?.Invoke();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        _ = Task.Delay(Debounce, token).ContinueWith(
            t => { if (!t.IsCanceled) _ = FlushAsync(); },
            TaskScheduler.Current);
    }

    /// <summary>Saves pending changes now; safe to call repeatedly.</summary>
    public async Task FlushAsync()
    {
        _debounce?.Cancel();
        while (IsDirty && Status is not (SaveStatus.Invalid or SaveStatus.Failed) || _saving is not null)
        {
            if (_saving is not null) { await _saving; continue; }
            _saving = SaveOnceAsync();
            try { await _saving; }
            finally { _saving = null; }
        }
    }

    private async Task SaveOnceAsync()
    {
        var snapshot = Draft;
        var seq = _changeSeq;

        var errors = RecipeValidator.Validate(snapshot, catalog.Lookup);
        if (!errors.IsValid)
        {
            SetStatus(SaveStatus.Invalid, errors.Values.First()[0]);
            return;
        }

        SetStatus(SaveStatus.Saving);
        var result = await catalog.SaveRecipeAsync(snapshot);
        switch (result)
        {
            case SaveResult<RecipeDto>.Saved { Value: var saved }:
                _savedSeq = seq;
                SavedAnything = true;
                // Keep edits made while the request was in flight; take the new version token.
                Draft = ReferenceEquals(Draft, snapshot) ? saved : Draft with { Version = saved.Version, UpdatedAt = saved.UpdatedAt };
                SetStatus(IsDirty ? SaveStatus.Pending : SaveStatus.Saved);
                break;
            case SaveResult<RecipeDto>.Conflict { Current: var current }:
                Draft = current;
                _savedSeq = _changeSeq;
                SetStatus(SaveStatus.Saved);
                toasts.Show("Рецепт змінили на іншому пристрої — показано новішу версію", ToastKind.Info);
                break;
            case SaveResult<RecipeDto>.Missing:
                _savedSeq = _changeSeq;
                SetStatus(SaveStatus.Failed, "Рецепт видалено");
                toasts.Show("Цей рецепт видалили на іншому пристрої", ToastKind.Error);
                break;
            case SaveResult<RecipeDto>.Invalid { Errors: var e }:
                SetStatus(SaveStatus.Invalid, e.Values.FirstOrDefault()?.FirstOrDefault() ?? "Помилка даних");
                break;
            case SaveResult<RecipeDto>.Failed { Message: var message }:
                SetStatus(SaveStatus.Failed, message);
                break;
        }
    }

    /// <summary>Takes the server's copy after a change made elsewhere (e.g. an ingredient was deleted).</summary>
    public void ReplaceFromServer(RecipeDto current)
    {
        _debounce?.Cancel();
        Draft = current;
        _savedSeq = _changeSeq;
        SetStatus(SaveStatus.Saved);
    }

    /// <summary>Retries after a failed save (e.g. the connection came back).</summary>
    public Task RetryAsync()
    {
        if (Status is SaveStatus.Failed or SaveStatus.Invalid) Status = SaveStatus.Pending;
        return FlushAsync();
    }

    private void SetStatus(SaveStatus status, string? message = null)
    {
        Status = status;
        StatusMessage = message;
        Changed?.Invoke();
    }

    public void Dispose() => _debounce?.Cancel();
}
