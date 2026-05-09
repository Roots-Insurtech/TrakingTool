using Microsoft.JSInterop;

namespace TrakingTool.Services;

/// <summary>
/// Espone lo stato online/offline del browser ascoltando i relativi eventi window.
/// L'inizializzazione richiede un contesto in cui JS interop sia disponibile
/// (es. da OnAfterRenderAsync di MainLayout).
/// </summary>
public sealed class OnlineStatusService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<OnlineStatusService>? _selfRef;
    private bool _initialized;

    public bool IsOnline { get; private set; } = true;

    /// <summary>Triggered ad ogni transizione online↔offline.</summary>
    public event Action<bool>? OnlineChanged;

    public OnlineStatusService(IJSRuntime js) => _js = js;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            IsOnline = await _js.InvokeAsync<bool>("ttOnline.register", _selfRef);
        }
        catch
        {
            // se il JS non è ancora stato caricato, riproveremo al prossimo render
            _initialized = false;
        }
    }

    [JSInvokable]
    public Task OnOnlineChange(bool isOnline)
    {
        if (IsOnline == isOnline) return Task.CompletedTask;
        IsOnline = isOnline;
        OnlineChanged?.Invoke(isOnline);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        _selfRef = null;
        return ValueTask.CompletedTask;
    }
}
