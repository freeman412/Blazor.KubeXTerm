using Blazor.KubeXTerm.Components;
using Microsoft.AspNetCore.Components;

namespace Blazor.KubeXTerm.Demo.Services
{
    public class KubeXTermTabPanelService
    {
        private readonly List<TabViewModel> _tabs = new();
        private int _activeTabIndex = 0;

        public event Action? TabsChanged;
        public event Action<int>? ActiveTabChanged;

        public IReadOnlyList<TabViewModel> Tabs => _tabs.AsReadOnly();
        public int ActiveTabIndex => _activeTabIndex;

        public void Initialize()
        {
            if (_tabs.Count == 0)
            {
                AddWelcomeTab();
            }
        }

        public void AddTab(string podName, string connectionType, string? command = null)
        {
            if (string.IsNullOrWhiteSpace(command))
                command = "/bin/bash";

            var cmdParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tabId = Guid.NewGuid();

            var newTab = new TabViewModel
            {
                Id = tabId,
                Label = podName,
                K8STerminalFragment = builder =>
                {
                    builder.OpenComponent<TerminalResizeHost>(0);
                    builder.AddAttribute(1, "HostId", tabId);
                    builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
                    {
                        child.OpenComponent<KubernetesTerminal>(0);
                        child.AddAttribute(1, "PodName", podName);
                        child.AddAttribute(2, "K8SContext", KubeXTermK8SManager.K8SClient);
                        child.AddAttribute(3, "ConnectionType", connectionType);
                        child.AddAttribute(4, "Command", cmdParts);
                        child.AddAttribute(5, "Style", "flex:1 1 auto;min-height:0; padding:5px");
                        child.AddAttribute(6, "SessionId", tabId);
                        child.CloseComponent();
                    }));
                    builder.CloseComponent();
                }
            };

            _tabs.Add(newTab);
            SetActiveTab(_tabs.Count - 1);
            TabsChanged?.Invoke();
        }

        public void RemoveTab(Guid tabId)
        {
            var tab = _tabs.FirstOrDefault(x => x.Id == tabId);
            if (tab == null) return;

            var indexToBeRemoved = _tabs.IndexOf(tab);
            _tabs.Remove(tab);

            // Adjust active tab index
            if (_activeTabIndex >= _tabs.Count)
            {
                _activeTabIndex = Math.Max(0, _tabs.Count - 1);
            }
            else if (_activeTabIndex > indexToBeRemoved)
            {
                _activeTabIndex--;
            }
            else if (_activeTabIndex == indexToBeRemoved && _tabs.Count > 0)
            {
                _activeTabIndex = Math.Min(_activeTabIndex, _tabs.Count - 1);
            }

            // If no tabs left, add welcome tab
            if (_tabs.Count == 0)
            {
                AddWelcomeTab();
            }

            TabsChanged?.Invoke();
            ActiveTabChanged?.Invoke(_activeTabIndex);
        }

        public void SetActiveTab(int index)
        {
            if (index >= 0 && index < _tabs.Count)
            {
                _activeTabIndex = index;
                ActiveTabChanged?.Invoke(_activeTabIndex);
            }
        }

        private void AddWelcomeTab()
        {
            var welcomeTab = new TabViewModel
            {
                Id = Guid.NewGuid(),
                Label = "Welcome",
                K8STerminalFragment = builder =>
                {
                    builder.OpenComponent<WelcomeTerminal>(0);
                    builder.CloseComponent();
                }
            };

            _tabs.Add(welcomeTab);
            _activeTabIndex = 0;
            TabsChanged?.Invoke();
        }
    }

    public class TabViewModel
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public RenderFragment K8STerminalFragment { get; set; } = null!;
        public bool ShowCloseIcon { get; set; } = false;
    }
}