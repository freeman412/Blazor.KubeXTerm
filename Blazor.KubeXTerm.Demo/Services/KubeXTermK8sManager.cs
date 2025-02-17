using k8s;

namespace Blazor.KubeXTerm.Demo.Services;

public class KubeXTermK8SManager
{
    public KubernetesClientConfiguration K8SConfig;
    public IKubernetes K8SClient;
}