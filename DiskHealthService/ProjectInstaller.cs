using System.ComponentModel;
using System.ServiceProcess;

namespace DiskHealthService
{
    [RunInstaller(true)] // Bu öznitelik yükleyici sınıfını belirtir.
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();

            // ServiceProcessInstaller (Hizmetin çalışma hesabı)
            var serviceProcessInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem // Hizmet, sistem hesabı ile çalışır.
            };

            // ServiceInstaller (Hizmetin adı ve başlangıç türü)
            var serviceInstaller = new ServiceInstaller
            {
                ServiceName = "DiskHealthService",
                DisplayName = "Disk Health Monitoring Service",
                StartType = ServiceStartMode.Automatic // Otomatik başlatma
            };

            // Yükleyicilere ekleme
            Installers.Add(serviceProcessInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
