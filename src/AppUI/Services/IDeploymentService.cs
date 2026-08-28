using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public interface IDeploymentService
    {
        string? FindNativeCoreDllPath();
        string? FindDefaultIniPath();

        Task<DeploymentResult> DeployAsync(string targetGameDirectory, DeploymentMode mode, string? customIniPath = null);
        Task<DeploymentResult> UninstallAsync(string targetGameDirectory, DeploymentMode mode);
        bool IsDeployed(string targetGameDirectory, DeploymentMode mode);
    }
}
