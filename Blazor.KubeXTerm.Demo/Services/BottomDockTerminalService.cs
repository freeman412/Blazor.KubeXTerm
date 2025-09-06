using Blazor.KubeXTerm.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor.Extensions.Components.DockedView;
using MudBlazor.Extensions.Services;

namespace Blazor.KubeXTerm.Demo.Services
{
    public class BottomDockTerminalService (BottomDockService bottomDockService)
    {

        public void AddTerminalToBottomDock(string podName, string connectionType, string? command = null)
        {
            if (string.IsNullOrWhiteSpace(command))
                command = "/bin/bash";
            var cmdParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var dockGuid = Guid.NewGuid();
            RenderFragment terminalFragment = builder =>
            {
                builder.OpenComponent<TerminalResizeHost>(0);
                builder.AddAttribute(1, "HostId", dockGuid);
                builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
                {
                    child.OpenComponent<KubernetesTerminal>(0);
                    child.AddAttribute(1, "PodName", podName);
                    child.AddAttribute(2, "K8SContext", KubeXTermK8SManager.K8SClient);
                    child.AddAttribute(3, "ConnectionType", connectionType);
                    child.AddAttribute(4, "Command", cmdParts);
                    child.AddAttribute(5, "Style", "flex:1 1 auto;min-height:0; padding:5px");
                    child.AddAttribute(6, "SessionId", dockGuid); // NEW: keep backend session across dock/undock
                    child.CloseComponent();
                }));
                builder.CloseComponent();
            };

            var dockedViewTab = new DockedViewTab
            {
                Title = $"{podName}",
                ChildContent = terminalFragment,
                Icon = MudBlazor.Icons.Material.Filled.Terminal,
                Description = $"{podName}"
            };

            bottomDockService.AddTab(dockedViewTab);
        }
    }
}
