using amethyst_installer_gui.Controls;
using amethyst_installer_gui.Installer.Modules.Checks;
using amethyst_installer_gui.PInvoke;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace amethyst_installer_gui.Installer.Modules {
    public class StoreModule : ModuleBase {
        public override bool Install(string sourceFile, string path, ref InstallModuleProgress control,
            out TaskState state) {
            bool success = true;

            try {
                Logger.Info(string.Format(LogStrings.InstallingExe,
                    InstallerStateManager.ModuleStrings[Module.Id].Title));
                control.LogInfo(string.Format(LogStrings.InstallingExe,
                    InstallerStateManager.ModuleStrings[Module.Id].Title));

                // Skip whole installation if the package is already installed
                if ( new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .Any(x => x.Id.Name == Module.Install.Items.FirstOrDefault()?.ToString()) ) {
                    state = TaskState.Checkmark;
                    return true; // Skip installation
                }

                try {
                    Task.Run(async () => {
                        try {
                            // Prompt the windows package manager to register our package
                            await new PackageManager().AddPackageAsync(
                                new Uri(Path.GetFullPath(Path.Combine(Constants.AmethystTempDirectory, sourceFile))), null, 
                                DeploymentOptions.ForceApplicationShutdown | DeploymentOptions.ForceUpdateFromAnyVersion);

                            success = true; // Assume everything is okay if no exceptions have been thrown
                        } catch ( Exception e ) {
                            Logger.Error(e);
                        }

                        success = false;
                    }).Wait(TimeSpan.FromSeconds(10));
                } catch ( Exception e ) {
                    Logger.Error(e);
                }

                // Try fetching the package name from JSON
                success = new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .Any(x => x.Id.Name == Module.Install.Items.FirstOrDefault()?.ToString());
            } catch ( Exception ex ) {
                Logger.Fatal(
                    $"{string.Format(LogStrings.FailedInstallExe, InstallerStateManager.ModuleStrings[Module.Id].Title)}:\n{Util.FormatException(ex)})");
                control.LogError(
                    $"{string.Format(LogStrings.FailedInstallExe, InstallerStateManager.ModuleStrings[Module.Id].Title)}! {LogStrings.ViewLogs}");
                success = false;
            }

            state = success ? TaskState.Checkmark : TaskState.Error;
            return success;
        }
    }
}