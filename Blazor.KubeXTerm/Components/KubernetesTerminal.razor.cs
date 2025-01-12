﻿//namespace Blazor.KubeXTerm.Components;

//using Microsoft.AspNetCore.Components.Web;
//using Microsoft.AspNetCore.Components;
//using Microsoft.JSInterop;
//using System.Net.WebSockets;
//using System.Text;
//using System.Text.Json;
//using XtermBlazor;
//using k8s;
//using Utils;
//using KubeXTerm.Services;


//public partial class KubernetesTerminal: ComponentBase, IAsyncDisposable
//{
//    [Inject]
//    internal IJSRuntime JSRuntime { get; set; } = default!;

//    [Parameter]
//    public string Namespace { get; set; } = "default";
//    [Parameter, EditorRequired]
//    public required string PodName { get; set; }
//    [Parameter]
//    public string? ContainerName { get; set; }
//    [Parameter, EditorRequired]
//    public required IKubernetes K8sContext { get; set; }
//    [Parameter, EditorRequired]
//    public required string ConnectionType { get; set; }

//    //The main Xterm Object
//    private Xterm Term;
//    KubeXTermConnectionManager KubeExecutor;
//    private TerminalOptions _options;

//    /// <summary>
//    /// Setup the Terminal Options based on what kind of request it is
//    /// </summary>
//    protected override void OnParametersSet()
//    {
//        bool _convertEOL = false;
//        bool _disableStdin = false;
//        // Update _options based on ConnectionType
//        if (ConnectionType != K8sConnectionType.BASH)
//        {
//            _disableStdin = true;
//            _convertEOL = true;
//        }

//        _options = new TerminalOptions
//        {
//            CursorBlink = true,
//            CursorStyle = CursorStyle.Bar,
//            FontFamily = "monospace",
//            ConvertEOL = _convertEOL,
//            DisableStdin = _disableStdin,

//            Theme =
//            {
//                Background = "#282a36",
//                Foreground = "#bacfc7"
//            },
//        };

//    }

//    //Change to take a parameter at some point - ideally would be an uploaded file? 
//    // For now default config works if running in k8s oro  using docker desktop kubernetes
//    KubernetesClientConfiguration k8sConfig = KubernetesClientConfiguration.BuildDefaultConfig();

//    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
//    private readonly HashSet<string> Addons = new HashSet<string> { "addon-fit" };
//    private bool _isConnected = false;
//    private DotNetObjectReference<KubernetesTerminal> dotNetObjectReference;

//    /// <summary>
//    /// Tracks the Viewport width for resize events
//    /// </summary>
//    public int ViewportWidth { get; set; }
//    /// <summary>
//    /// Tracks the Viewport height for resize events
//    /// </summary>
//    public int ViewportHeight { get; set; }

//    /// <summary>
//    /// Method called from the javascript watcher for user initiated resizing
//    /// </summary>
//    /// <param name="width"></param>
//    /// <param name="height"></param>
//    /// <returns></returns>
//    [JSInvokable]
//    public async Task OnResize(int width, int height)
//    {
//        if (ViewportWidth == width && ViewportHeight == height)
//            return;
//        ViewportWidth = width;
//        ViewportHeight = height;
//        await FitTerminal();
//        StateHasChanged();
//    }


//    protected override async Task OnAfterRenderAsync(bool firstRender)
//    {
//        try
//        {
//            if (firstRender)
//            {
//                dotNetObjectReference = DotNetObjectReference.Create(this);
//                await JSRuntime.InvokeVoidAsync("window.registerViewportChangeCallback", dotNetObjectReference);
//            }

//            if (KubeExecutor != null)
//            {
//                int rows = await Term.GetRows();
//                int cols = await Term.GetColumns();
//                await KubeExecutor.SendResizeCommandAsync(rows, cols);
//            }
//        }
//        catch (Exception e)
//        {
//            //Not a big deal, the component is gone
//            Console.WriteLine($"Error in OnAfterRenderAsync: {e.Message}");
//        }
//    }

//    /// <summary>
//    /// The first render method for the XTerm Component. Sets up the connection to k8s and fits the terminal to the window.
//    /// </summary>
//    /// <returns></returns>
//    private async Task OnFirstRender()
//    {
//        //Initialize the kubernetes executor
//        KubeExecutor = new KubeXTermConnectionManager(Term, K8sContext, Namespace);
//        await FitTerminal();

//        if (ConnectionType != K8sConnectionType.BASH)
//        {
//            _options.DisableStdin = true;
//            _options.ConvertEOL = true;
//        }


//        await StartTerminalConnection();

//    }

//    private async Task FitTerminal()
//    {
//        //Tell the javacript XTerm to fit the container
//        await Term.Addon("addon-fit").InvokeVoidAsync("fit");

//        //Tell websocket in kubernetes about the resize
//        if (KubeExecutor != null)
//        {
//            int rows = await Term.GetRows();
//            int cols = await Term.GetColumns();
//            await KubeExecutor.SendResizeCommandAsync(rows, cols);
//        }
//    }

//    /// <summary>
//    /// Sets up the terminal connection based on requested type
//    /// </summary>
//    /// <returns></returns>
//    private async Task StartTerminalConnection()
//    {
//        if (ConnectionType == K8sConnectionType.BASH)
//            await KubeExecutor.ExecInPod(PodName, ContainerName);
//        else if (ConnectionType == K8sConnectionType.STDOUT)
//            await KubeExecutor.StdOutInPod(PodName, ContainerName);
//        else if (ConnectionType == K8sConnectionType.LOGS)
//            await KubeExecutor.LogsInPodAync(PodName, ContainerName);
//        else if (ConnectionType == K8sConnectionType.STDERR)
//            await KubeExecutor.StdErrInPod(PodName, ContainerName);
//        else if (ConnectionType == K8sConnectionType.ALLLOGS)
//            await KubeExecutor.AllLogsAsync(PodName, ContainerName);
//    }

//    /// <summary>
//    /// Anytime anything is put into the XTerm, it immediately gets written 
//    /// here from the OnData function
//    /// </summary>
//    /// <param name="data"></param>
//    /// <returns></returns>
//    private async Task WriteToTerminal(string data)
//    {
//        await KubeExecutor.WriteStream(data);
//    }

//    /// <summary>
//    /// 
//    /// </summary>


//    public async ValueTask DisposeAsync()
//    {
//        try
//        {
//            // Dispose of JSInterop callbacks
//            if (dotNetObjectReference != null)
//            {
//                dotNetObjectReference.Dispose();
//                await JSRuntime.InvokeVoidAsync("window.unregisterViewportChangeCallback");
//            }
//            // Dispose of the KubeExecutor resource
//            KubeExecutor?.DisposeAsync();
//        }
//        catch (Exception ex)
//        {
//            // Handle any exceptions that might occur during stream write operations
//            Console.WriteLine($"Error while Disposing of Terminal: {ex.Message}");
//        }

//        // Cancel and dispose of the cancellation token source
//        _cancellationTokenSource?.Cancel();
//        _cancellationTokenSource?.Dispose();
//    }
//}

