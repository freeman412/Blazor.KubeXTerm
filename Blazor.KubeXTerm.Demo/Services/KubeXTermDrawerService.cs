using Blazor.KubeXTerm.Components;
using Blazor.KubeXTerm.Demo.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor.Extensions.Models;
using MudBlazor.Extensions.Services;

namespace Blazor.KubeXTerm.Demo.Services;

public class KubeXTermDrawerService
{
    private readonly DrawerService _drawerService;
    public KubeXTermDrawerService(DrawerService drawerService) => _drawerService = drawerService;

    public event Action? DrawersChanged;

    // Tune these defaults to match what your BottomDrawer expects.
    private const double DefaultWidth = 520;   // px
    private const double DefaultHeight = 340;  // px

    public void AddTerminalToDrawer(string podName, string connectionType, string? command = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            command = "/bin/bash";

        var cmdParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var drawerGuid = Guid.NewGuid();

        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<TerminalDrawerHost>(0);
            builder.AddAttribute(1, "DrawerGuid", drawerGuid);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<KubernetesTerminal>(0);
                child.AddAttribute(1, "PodName", podName);
                child.AddAttribute(2, "K8SContext", KubeXTermK8SManager.K8SClient);
                child.AddAttribute(3, "ConnectionType", connectionType);
                child.AddAttribute(4, "Command", cmdParts);
                child.AddAttribute(5, "Style", "flex:1 1 auto;min-height:0; padding:5px");
                child.AddAttribute(6, "SessionId", drawerGuid); // NEW: keep backend session across dock/undock
                child.CloseComponent();
            }));
            builder.CloseComponent();
        };

        var model = new DrawerModel
        {
            DrawerGuid = drawerGuid,
            ChildContent = fragment,
            Title = $"{podName}",
            InitialWidth = DefaultWidth,
            InitialHeight = DefaultHeight,
            CurrentWidth = DefaultWidth,      // Seed so stacking math is correct immediately
            //CurrentHeight = DefaultHeight
        };

        _drawerService.AddDrawer(model);
        DrawersChanged?.Invoke();
    }

    public IReadOnlyList<DrawerModel> Current => _drawerService.Drawers;
}
