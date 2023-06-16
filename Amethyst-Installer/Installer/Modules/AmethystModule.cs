using amethyst_installer_gui.Controls;
using amethyst_installer_gui.PInvoke;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Management.Deployment;
using Windows.System;

namespace amethyst_installer_gui.Installer.Modules {
    public class AmethystModule : ModuleBase {
        public AmethystModule() { }

        /*
         
if upgrade yes
   delete old files in folder according to funny filelist
   extract new ame zip
   update uninstall entry version number
if upgrade no
   extract ame zip to new folder
   register uninstall entry
   verify that there are no other entries in vrpaths named amethyst, if so remove them
   register ame driver entry in steamvr
   register tracker roles in vrsettings
         
         */

        public override bool Install(string sourceFile, string path, ref InstallModuleProgress control,
            out TaskState state) {
            InstallUtil.TryKillingConflictingProcesses();

            if ( AddUntrustedDesktopPackage(ref control, Path.GetFullPath(
                    Path.Combine(Constants.AmethystTempDirectory, sourceFile))) ) {
                if ( !HandleInstallerPersistence(ref control) ) {
                    // If we can't install to appdata you have bigger problems to deal with
                    state = TaskState.Error;
                    return false;
                }

                bool overallSuccess = true;
                if ( !InstallerStateManager.IsUpdating ) {
                    if ( InstallerStateManager.SteamVRInstalled ) {
                        overallSuccess = HandleDrivers(path, ref control);
                    }

                    // In some cases vrpathreg might open SteamVR, which we don't want; Kill it instantly!
                    InstallUtil.TryKillingConflictingProcesses();
                }

                bool successMinor = true;
                if ( InstallerStateManager.SteamVRInstalled ) {
                    successMinor = AssignTrackerRoles(ref control);
                }

                successMinor = successMinor && RegisterProtocolLink(path, ref control);
                successMinor = successMinor && UpdateFirewallRules(ref control);

                if ( !InstallerStateManager.IsUpdating ) {
                    if ( InstallerStateManager.SteamVRInstalled ) {
                        successMinor = successMinor && AdjustSteamVrSettings(ref control);
                    }

                    // Don't recreate shortcuts during an update!
                    overallSuccess = overallSuccess && CreateShortcuts(path, ref control);
                    // Assign default settings
                    overallSuccess = overallSuccess && SetDefaultEndpoint(path, ref control);
                }

                // I hate K2EX
                successMinor = successMinor && NukeK2EX(ref control);

                // @TODO: If this is an upgrade change the message to a different one
                Logger.Info(LogStrings.InstalledAmethystSuccess);
                control.LogInfo(LogStrings.InstalledAmethystSuccess);
                state = successMinor ? TaskState.Checkmark : TaskState.Warning;
                return overallSuccess;
            }

            state = TaskState.Error;
            return false;
        }

        private bool AddPackageCertificateAsync(ref InstallModuleProgress control, string packagePath) {
            try {
                control.LogInfo("Extracting X509 certificate from the bundle...");
                X509Certificate cert = X509Certificate.CreateFromSignedFile(packagePath);
                using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);

                control.LogInfo("Writing the certificate to the root store...");
                store.Open(OpenFlags.ReadWrite); // Fails without admin cause RW (exception)
                store.Add(new X509Certificate2(cert)); // Adds it to the trusted CA store

                return true; // Assume everything is okay if no exceptions have been thrown
            } catch ( Exception e ) {
                Logger.Error(e.Message);
            }

            return false;
        }

        private bool AddUntrustedDesktopPackage(ref InstallModuleProgress control, string packagePath) {
            if ( new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                .Any(x => x.Id.Name is "K2VRTeam.Amethyst.App") ) {
                control.LogInfo("Amethyst (K2VRTeam.Amethyst.App) is already installed!");
                control.LogInfo("(If you wanted to reinstall, please uninstall it manually first)");
                return true; // Already done before
            }

            if ( !AddPackageCertificateAsync(ref control, packagePath) ) {
                control.LogInfo("Couldn't install the package certificate!");
                return false;
            }

            try {
                // Prepare the installation task
                control.LogInfo("Registering the downloaded msix-appx package...");
                var installTask = Task.Run(async () => {
                    try {
                        Logger.Info($"Adding a store package from \"{packagePath}\"...");
                        var result = await new PackageManager().AddPackageAsync(new Uri(packagePath), null,
                            DeploymentOptions.ForceApplicationShutdown | DeploymentOptions.ForceUpdateFromAnyVersion);

                        Logger.Info($"Registration result: {result.IsRegistered} {result.ErrorText}");
                        return result.IsRegistered; // Return the outer deployment result
                    } catch ( Exception e ) {
                        Logger.Error(e);
                    }

                    return false;
                });

                // Wait for the installation to finish
                installTask.Wait(TimeSpan.FromSeconds(30));
                var result = installTask.Result; // Capture

                Logger.Info($"Installed with result: {result}");
                return result && new PackageManager() // Also self-check
                    .FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .Any(x => x.Id.Name is "K2VRTeam.Amethyst.App");
            } catch ( Exception e ) {
                Logger.Error(e.Message);
            }

            return false;
        }

        private bool HandleDrivers(string path, ref InstallModuleProgress control) {
            if ( !new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .Any(x => x.Id.Name is "K2VRTeam.Amethyst.App") ) {
                return false; // Amethyst is not installed, give up on everything
            }

            // Get the driver path
            string driverPath = Path.Combine(
                Path.GetFullPath(Path.Combine(Constants.Userprofile, "AppData", "Local")), "Packages",
                new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .First(x => x.Id.Name is "K2VRTeam.Amethyst.App").Id.FamilyName,
                "LocalState", "Amethyst");

            // Make the driver folder public
            Task.Run(async () => await CopyPackagedDriver(driverPath)).Wait();

            Logger.Info(LogStrings.CheckingAmethystDriverConflicts);
            control.LogInfo(LogStrings.CheckingAmethystDriverConflicts);

            // Check for K2EX driver
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("KinectToVR")) ) {
                OpenVRUtil.ForceDisableDriver("KinectToVR");
                OpenVRUtil.RemoveDriversWithName("KinectToVR");
            }

            Logger.Info(LogStrings.RegisteringAmethystDriver);
            control.LogInfo(LogStrings.RegisteringAmethystDriver);

            // Check for existing Amethyst driver entries
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("Amethyst")) ) {
                OpenVRUtil.RemoveDriversWithName("Amethyst");
            }

            if ( OpenVRUtil.ConnectionType == VRConnectionType.ALVR ) {
                Logger.Info("Registering ALVR addon...");
                control.LogInfo(LogStrings.RegisteringAlvrDriver);
                OpenVRUtil.RegisterSteamVrDriver(OpenVRUtil.AlvrInstallPath);
            }

            OpenVRUtil.RegisterSteamVrDriver(driverPath);
            Logger.Info("Force enabling addon");
            OpenVRUtil.ForceEnableDriver("Amethyst");

            Logger.Info("Killing conflicting processes (thanks Valve)");
            InstallUtil.TryKillingConflictingProcesses();
            Logger.Info("Killed all conflicting processes!");

            return true;
        }

        private async Task<bool> CopyPackagedDriver(string expectedDriverPath) {
            // Tell Amethyst to make the driver folder public
            Logger.Info("Prompting Amethyst to make driver files public...");
            await Launcher.LaunchUriAsync(
                new Uri(@"amethyst-app:make-local?source=Plugins\plugin_OpenVR\Driver\Amethyst"));

            // Wait until it's copied and check the result
            await Task.Delay(1000); // I know it's kinda ugly, but so am I...

            // Check whether the copied driver is there and return
            if ( ValidateDriverFolder(expectedDriverPath) ) {
                return true;
            }

            /* Manually copy all driver files */

            // Get the packaged driver path
            DirectoryInfo packagedDriverFolder = new(Path.Combine(
                InstallerStateManager.AmethystVirtualRootDirectory, "Plugins", "plugin_OpenVR", "Driver", "Amethyst"));

            Logger.Info("Validation failed! Manually copying all driver files...");

            // Now Create all of the directories
            foreach ( DirectoryInfo dirPath in packagedDriverFolder.GetDirectories("*", SearchOption.AllDirectories) ) {
                Directory.CreateDirectory(dirPath.FullName.Replace(packagedDriverFolder.FullName, expectedDriverPath));
            }

            // Copy all the files & Replaces any files with the same name
            foreach ( FileInfo newPath in packagedDriverFolder.GetFiles("*.*", SearchOption.AllDirectories) ) {
                newPath.CopyTo(newPath.FullName.Replace(packagedDriverFolder.FullName, expectedDriverPath), true);
            }

            // Check whether the copied driver is there and return
            return ValidateDriverFolder(expectedDriverPath);
        }

        private bool ValidateDriverFolder(string path) {
            Logger.Info($"Validating Amethyst driver files in \"{path}\"...");
            if ( !Directory.Exists(path) ) return false;

            FileInfo[] driverFiles = new DirectoryInfo(path).GetFiles("*.*", SearchOption.AllDirectories);
            return driverFiles.Any(x => x.Name is "driver.vrdrivermanifest") &&
                   driverFiles.Any(x => x.Name is "driver_Amethyst.dll") &&
                   driverFiles.Any(x => x.Name is "Amethyst.Plugins.Contract.dll");
        }

        private bool AssignTrackerRoles(ref InstallModuleProgress control) {
            bool success = true;
            {
                Logger.Info(LogStrings.AssigningTrackerRoles);
                control.LogInfo(LogStrings.AssigningTrackerRoles);

                try {
                    // Assign all Amethyst tracker roles
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-00WAIST0",
                        TrackerRole.TrackerRole_Waist);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-00WAIST00",
                        TrackerRole.TrackerRole_Waist);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-CHEST", TrackerRole.TrackerRole_Chest);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0ELBOW0",
                        TrackerRole.TrackerRole_LeftElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0FOOT00",
                        TrackerRole.TrackerRole_LeftFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-L0KNEE00",
                        TrackerRole.TrackerRole_LeftKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LELBOW",
                        TrackerRole.TrackerRole_LeftElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LFOOT",
                        TrackerRole.TrackerRole_LeftFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-LKNEE",
                        TrackerRole.TrackerRole_LeftKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0ELBOW0",
                        TrackerRole.TrackerRole_RightElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0FOOT00",
                        TrackerRole.TrackerRole_RightFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-R0KNEE00",
                        TrackerRole.TrackerRole_RightKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RELBOW",
                        TrackerRole.TrackerRole_RightElbow);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RFOOT",
                        TrackerRole.TrackerRole_RightFoot);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-RKNEE",
                        TrackerRole.TrackerRole_RightKnee);
                    OpenVRUtil.SetTrackerRole("/devices/amethyst/vr_tracker/AME-WAIST", TrackerRole.TrackerRole_Waist);
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailAssignTrackerRoles}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailAssignTrackerRoles}:\n{Util.FormatException(e)})");
                    success = success && false;
                }
            }

            {
                Logger.Info(LogStrings.RemovingConflictingTrackerRoles);
                control.LogInfo(LogStrings.RemovingConflictingTrackerRoles);

                try {
                    // Ancient versions of KinectToVR
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/0");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/1");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/2");
                    OpenVRUtil.RemoveTrackerRole("/devices/00vrinputemulator/3");

                    // For most of K2EX's lifespan we tried mimicking Vive Trackers, sorry cnlohr (pls don't look at the OpenVrUtils files k thx)
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB11ABEC");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB1441A7");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T0");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T1");
                    OpenVRUtil.RemoveTrackerRole("/devices/htc/vive_trackerLHR-CB9AD1T2");

                    // In K2EX 0.9.1 we use custom serial ids
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_HIP");
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_LFOOT");
                    OpenVRUtil.RemoveTrackerRole("/devices/KinectToVR/Puck_RFOOT");
                } catch ( Exception e ) {
                    control.LogError($"{LogStrings.FailRemoveConflictingTrackerRoles}! {LogStrings.ViewLogs}");
                    Logger.Fatal($"{LogStrings.FailRemoveConflictingTrackerRoles}:\n{Util.FormatException(e)})");
                    success = success && false;
                }
            }

            return success;
        }

        private bool CreateShortcuts(string path, ref InstallModuleProgress control) {
            // Desktop app shortcut
            if ( !InstallerStateManager.CreateDesktopShortcut ) return true;

            Logger.Info(LogStrings.CreatingDesktopEntry);
            control.LogInfo(LogStrings.CreatingDesktopEntry);

            try {
                Util.CreateStoreAppShortcut(Path.GetFullPath(@"C:\Users\Public\Desktop"),
                    $"{new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid()).First(x => x.Id.Name is "K2VRTeam.Amethyst.App").Id.FamilyName}!Amethyst");
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailedCreateDesktopEntry}! {LogStrings.ViewLogs}");
                Logger.Fatal($"{LogStrings.FailedCreateDesktopEntry}:\n{Util.FormatException(e)})");
            }

            return true;
        }

        private bool HandleInstallerPersistence(ref InstallModuleProgress control) {
            try {
                control.LogInfo(LogStrings.CreatingUpgradeList);
                Logger.Info(LogStrings.CreatingUpgradeList);

                // Write shit to appdata so that the installer will persist
                string installerConfigPath =
                    Path.GetFullPath(Path.Combine(Constants.AmethystConfigDirectory, "Modules.json"));
                Dictionary<string, int> moduleList = new();
                foreach ( Module module in InstallerStateManager.ModulesToInstall ) {
                    // Only track visible modules
                    if ( module.Visible ) {
                        moduleList.Add(module.Id, module.InternalVersion);
                    }
                }

                // Concat with existing JSON
                if ( File.Exists(installerConfigPath) ) {
                    // The file exists! Try reading it
                    try {
                        string fileContents = File.ReadAllText(installerConfigPath);
                        Dictionary<string, int> config =
                            JsonConvert.DeserializeObject<Dictionary<string, int>>(fileContents);
                        foreach ( KeyValuePair<string, int> item in config ) {
                            if ( !moduleList.ContainsKey(item.Key) ) {
                                moduleList.Add(item.Key, item.Value);
                            }
                        }
                    } catch ( Exception e ) {
                        Logger.Fatal($"Failed to load or parse file \"{installerConfigPath}\"!");
                        Logger.Fatal(Util.FormatException(e));
                    }
                }

                string jsonModules = JsonConvert.SerializeObject(moduleList, Formatting.Indented);
                File.WriteAllText(installerConfigPath, jsonModules);

                return true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.CreateInstallerListsFailed}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.CreateInstallerListsFailed);
                Logger.Fatal(Util.FormatException(e));
                return false;
            }
        }

        private bool AdjustSteamVrSettings(ref InstallModuleProgress control) {
            bool overallSuccess = true;

            try {
                control.LogInfo(LogStrings.DisablingSteamVrHome);
                Logger.Info(LogStrings.DisablingSteamVrHome);

                OpenVRUtil.DisableSteamVrHome();
                overallSuccess = true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailDisableSteamVrHome}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailDisableSteamVrHome);
                Logger.Fatal(Util.FormatException(e));
                overallSuccess = false;
            }

            try {
                control.LogInfo(LogStrings.EnablingSteamVrAdvancedSettings);
                Logger.Info(LogStrings.EnablingSteamVrAdvancedSettings);

                OpenVRUtil.EnableAdvancedSettings();

                overallSuccess = overallSuccess && true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailEnableSteamVrAdvancedSettings}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailEnableSteamVrAdvancedSettings);
                Logger.Fatal(Util.FormatException(e));
                overallSuccess = overallSuccess && false;
            }

            return overallSuccess;
        }

        private bool RegisterProtocolLink(string target, ref InstallModuleProgress control) {
            string amethystInstallerExecutable = Path.GetFullPath(Path.Combine(target, "AmethystUtils.exe"));

            try {
                control.LogInfo(LogStrings.RegisteringAmethystProtocolLink);
                Logger.Info(LogStrings.RegisteringAmethystProtocolLink);

                // Get root key
                RegistryKey amethystKey = WindowsUtils.GetKey(Registry.ClassesRoot, "amethyst", true);

                // Write to root key
                amethystKey.SetValue(string.Empty, $"URL:Amethyst Protocol", RegistryValueKind.String); // (Default)
                amethystKey.SetValue("ProtocolVersion", 1, RegistryValueKind.DWord); // ProtocolVersion
                amethystKey.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

                // Icon
                RegistryKey iconKey = WindowsUtils.GetKey(amethystKey, "DefaultIcon", true);
                iconKey.SetValue(string.Empty, "Amethyst.exe,0", RegistryValueKind.String);

                // Command
                RegistryKey shellKey = WindowsUtils.GetKey(amethystKey, "shell", true);
                RegistryKey openKey = WindowsUtils.GetKey(shellKey, "open", true);
                RegistryKey commandKey = WindowsUtils.GetKey(openKey, "command", true);
                commandKey.SetValue(string.Empty, $"\"{amethystInstallerExecutable}\" \"%1\" %*",
                    RegistryValueKind.String);

                return true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.FailRegisterAmethystProtocolLink}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.FailRegisterAmethystProtocolLink);
                Logger.Fatal(Util.FormatException(e));
                return false;
            }
        }

        private bool UpdateFirewallRules(ref InstallModuleProgress control) {
            control.LogInfo(LogStrings.UpdatingFirewallRules);
            Logger.Info(LogStrings.UpdatingFirewallRules);

            try {
                // Amethyst:: gRPC protocol ports
                bool success = Util.ActivateFirewallRule("Amethyst SteamVR Addon", NetworkProtocol.TCP, 7135);
                // owoTrack:: Rotational data default port
                success = success && Util.ActivateFirewallRule("owoTrack Rotation", NetworkProtocol.UDP, 6969);
                // owoTrack:: Info server allowing automatic discovery
                success = success && Util.ActivateFirewallRule("owoTrack Discovery", NetworkProtocol.UDP, 35903);
                // VRChat:: OSC
                success = success && Util.ActivateFirewallRule("VRChat outgoing data", NetworkProtocol.UDP, 9000);
                success = success && Util.ActivateFirewallRule("VRChat incoming data", NetworkProtocol.UDP, 9001);

                if ( success ) {
                    control.LogInfo(LogStrings.UpdatingFirewallRulesSuccess);
                    Logger.Info(LogStrings.UpdatingFirewallRulesSuccess);
                } else {
                    control.LogError(LogStrings.UpdatingFirewallRulesFailure + "!");
                    Logger.Fatal(LogStrings.UpdatingFirewallRulesFailure + "!");
                }

                return success;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.UpdatingFirewallRulesFailure}! {LogStrings.ViewLogs}");
                Logger.Fatal(LogStrings.UpdatingFirewallRulesFailure);
                Logger.Fatal(Util.FormatException(e));
                return false;
            }
        }

        private bool SetDefaultEndpoint(string path, ref InstallModuleProgress control) {
            /* PluginDefaults.json
             {
                "TrackingDevice": "K2VRTEAM-AME2-APII-DVCE-DVCEPSMOVEEX",
                "ServiceEndpoint": "K2VRTEAM-AME2-APII-SNDP-SENDPTOPENVR",
                "ExtraTrackers": true
             }
             */
            try {
                var targetServiceGuid = InstallerStateManager.DefaultToOSC
                    ? Constants.AmethystPluginGuidOSC
                    : Constants.AmethystPluginGuidOpenVR;

                // Check whether Amethyst is installed, as a boilerplate
                if ( !new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                        .Any(x => x.Id.Name is "K2VRTeam.Amethyst.App") ) {
                    return false; // Amethyst is not installed, give up
                }

                // First, try letting Amethyst set up its own defaults
                Task.Run(async () => await Launcher.LaunchUriAsync(
                    new Uri($"amethyst-app:set-defaults?ServiceEndpoint={targetServiceGuid}"))).Wait();

                // Validate the result and try re-doing that manually
                var defaultsFile = Path.Combine(Path.GetFullPath(Path.Combine(Constants.Userprofile, "AppData", "Local")), 
                    "Packages", new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                        .First(x => x.Id.Name is "K2VRTeam.Amethyst.App").Id.FamilyName, "LocalState", "PluginDefaults.json");

                // Get the default config path
                if ( File.Exists(defaultsFile) ) return true;

                JObject defaultConfig = new();
                defaultConfig["ServiceEndpoint"] = targetServiceGuid;
                
                control.LogInfo(LogStrings.SettingDefaultConfig);
                Logger.Info(string.Format(LogStrings.SettingDefaultConfigVerbose, defaultsFile));

                File.WriteAllText(defaultsFile, defaultConfig.ToString(Formatting.Indented));

                control.LogInfo(LogStrings.SettingDefaultConfigSuccess);
                Logger.Info(LogStrings.SettingDefaultConfigSuccess);

                return true;
            } catch ( Exception e ) {
                control.LogError($"{LogStrings.SettingDefaultConfigFailure}! {LogStrings.ViewLogs}");
                Logger.Fatal($"{LogStrings.SettingDefaultConfigFailure}:\n{Util.FormatException(e)})");
            }

            return false;
        }

        private bool NukeK2EX(ref InstallModuleProgress control) {
            if ( InstallerStateManager.K2EXDetected ) {
                control.LogInfo(LogStrings.K2EXUninstallStart);
                Logger.Info(LogStrings.K2EXUninstallStart);

                bool result = K2EXUtil.NukeK2EX(InstallerStateManager.K2EXPath);
                if ( result ) {
                    control.LogInfo(LogStrings.K2EXUninstallSuccess);
                    Logger.Info(LogStrings.K2EXUninstallSuccess);
                } else {
                    control.LogError(LogStrings.K2EXUninstallFailure);
                    Logger.Fatal(LogStrings.K2EXUninstallFailure);
                }

                return result;
            }

            return true;
        }
    }
}