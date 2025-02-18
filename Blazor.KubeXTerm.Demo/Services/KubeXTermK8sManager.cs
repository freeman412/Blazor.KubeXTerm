using k8s;
using k8s.Models;
using MudBlazor;

namespace Blazor.KubeXTerm.Demo.Services;

public class KubeXTermK8SManager
{
    private KubernetesClientConfiguration _k8SConfig = KubernetesClientConfiguration.BuildDefaultConfig();
    public static IKubernetes? K8SClient;

    public KubeXTermK8SManager()
    {
        K8SClient = new Kubernetes(_k8SConfig);
    }

    /// <summary>
    ///  Deletes a Kubernetes Pod.
    /// </summary>
    /// <param name="pod"></param>
    public async Task<bool> DeleteK8SPodAsync(V1Pod pod)
    {
        try
        {
            // Delete the pod from the specified namespace
            await KubeXTermK8SManager.K8SClient.CoreV1.DeleteNamespacedPodAsync(
                name: pod.Name(),
                namespaceParameter: pod.Namespace());

            Console.WriteLine($"Pod '{pod.Name()}' deleted successfully from namespace '{pod.Namespace()}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting pod '{pod.Name()}': {ex.Message}");
            return false;
        }
    }
    
    public string GetEnvValueFromV1Pod(V1Pod pod, string variableName)
    {
        string? val = pod.Spec?.Containers?.FirstOrDefault()?.Env?.FirstOrDefault(e => e.Name == variableName)?.Value;
        return val ?? "";
    }
    
    public string GetContainerState(V1ContainerStatus containerStatus)
    {
        if (containerStatus.State?.Waiting != null)
            return $"Waiting: {containerStatus.State.Waiting.Reason}";
        if (containerStatus.State?.Running != null)
            return "Running";
        if (containerStatus.State?.Terminated != null)
            return $"Terminated: {containerStatus.State.Terminated.Reason}";

        return "Unknown";
    }
}