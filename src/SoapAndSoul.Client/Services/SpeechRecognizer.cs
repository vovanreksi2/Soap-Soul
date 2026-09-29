using Microsoft.JSInterop;

namespace SoapAndSoul.Client.Services;

/// <param name="Final">Newly recognized text that will not change any more (append it).</param>
/// <param name="Interim">Text still being recognized (replace the previous interim text).</param>
public sealed record SpeechUpdate(string Final, string Interim);

/// <summary>
/// Speech-to-text on the device. The browser implementation uses the Web Speech API and asks for
/// on-device processing where supported; another engine (e.g. Whisper in WebAssembly) can implement
/// the same interface.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    bool IsListening { get; }

    /// <summary>True when audio is processed on the device rather than by the browser's cloud service.</summary>
    bool IsOnDevice { get; }

    event Action<SpeechUpdate>? Updated;

    /// <summary>Listening ended; carries a user-facing error, or null when it simply stopped.</summary>
    event Action<string?>? Stopped;

    Task<bool> IsSupportedAsync();

    Task StartAsync(string language = "uk-UA");

    Task StopAsync();
}

public sealed class BrowserSpeechRecognizer(IJSRuntime js) : ISpeechRecognizer
{
    private DotNetObjectReference<BrowserSpeechRecognizer>? _self;
    private IJSObjectReference? _session;

    public bool IsListening { get; private set; }
    public bool IsOnDevice { get; private set; }

    public event Action<SpeechUpdate>? Updated;
    public event Action<string?>? Stopped;

    public async Task<bool> IsSupportedAsync() => await js.InvokeAsync<bool>("soapAndSoul.speech.supported");

    public async Task StartAsync(string language = "uk-UA")
    {
        if (IsListening) return;
        _self ??= DotNetObjectReference.Create(this);
        _session = await js.InvokeAsync<IJSObjectReference>("soapAndSoul.speech.start", _self, language);
        IsOnDevice = await _session.InvokeAsync<bool>("isOnDevice");
        IsListening = true;
    }

    public async Task StopAsync()
    {
        if (_session is not null) await _session.InvokeVoidAsync("stop");
    }

    [JSInvokable]
    public void OnResult(string final, string interim) => Updated?.Invoke(new SpeechUpdate(final, interim));

    [JSInvokable]
    public void OnEnd(string? error)
    {
        IsListening = false;
        Stopped?.Invoke(error is null or "aborted" ? null : Describe(error));
    }

    private static string Describe(string error) => error switch
    {
        "not-allowed" or "service-not-allowed" => "Немає дозволу на мікрофон",
        "no-speech" => "Нічого не почуто — спробуйте ще раз",
        "audio-capture" => "Мікрофон недоступний",
        "network" => "Розпізнавання потребує мережі на цьому пристрої",
        "language-not-supported" => "Браузер не розпізнає українську",
        _ => "Розпізнавання перервано",
    };

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            try
            {
                await _session.InvokeVoidAsync("abort");
                await _session.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        _self?.Dispose();
    }
}
