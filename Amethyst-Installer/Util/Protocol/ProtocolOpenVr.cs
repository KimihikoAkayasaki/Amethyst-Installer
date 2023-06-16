using amethyst_installer_gui.Installer;
using amethyst_installer_gui.PInvoke;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace amethyst_installer_gui.Protocol {
    public class ProtocolRegister : IProtocolCommand {
        public string Command { get => "register"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"register\"!");

            DirectoryInfo ameDir = new( Path.Combine(InstallerStateManager.AmethystPackageDataDirectory, "LocalState", "Amethyst"));

            if ( !ameDir.Exists ) {
                Logger.Info("Failed to locate Amethyst driver install! Copying its files now...");
                if (!CopyPackagedDriver(ameDir.FullName)) Util.ShowMessageBox("Failed to locate Amethyst!", "Oops");
                return true;
            } else {
                Logger.Info($"Amethyst driver install found at {ameDir}");
            }

            // Check for existing Amethyst add-on entries
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("Amethyst")) ) {
                Logger.Info("Found multiple Amethyst add-ons! Removing...");
                OpenVRUtil.RemoveDriversWithName("Amethyst");
            }

            // Check for K2EX add-on, because of conflicts
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("KinectToVR")) ) {
                Logger.Info("K2EX add-on found! Removing...");
                OpenVRUtil.ForceDisableDriver("KinectToVR");
                OpenVRUtil.RemoveDriversWithName("KinectToVR");
            }

            Logger.Info(LogStrings.RegisteringAmethystDriver);
            
            OpenVRUtil.RegisterSteamVrDriver(ameDir.FullName);
            OpenVRUtil.ForceEnableDriver("Amethyst");

            InstallUtil.TryKillingConflictingProcesses();

            Util.ShowMessageBox("Successfully re-registered Amethyst SteamVR add-on!", "Success");
            return true;
        }

        private static bool CopyPackagedDriver(string expectedDriverPath) {

            bool ValidateDriverFolder(string path) {
                Logger.Info($"Validating Amethyst driver files in \"{path}\"...");
                if ( !Directory.Exists(path) ) return false;

                FileInfo[] driverFiles = new DirectoryInfo(path).GetFiles("*.*", SearchOption.AllDirectories);
                return driverFiles.Any(x => x.Name is "driver.vrdrivermanifest") &&
                       driverFiles.Any(x => x.Name is "driver_Amethyst.dll") &&
                       driverFiles.Any(x => x.Name is "Amethyst.Plugins.Contract.dll");
            }

            // Check whether the copied driver is there and return
            if ( ValidateDriverFolder(expectedDriverPath) ) {
                return true;
            }

            // Tell Amethyst to make the driver folder public
            Logger.Info("Prompting Amethyst to make driver files public...");
            Task.Run(async () => await Launcher.LaunchUriAsync(
                new Uri(@"amethyst-app:make-local?source=Plugins\plugin_OpenVR\Driver\Amethyst"))).Wait();

            // Wait until it's copied and check the result
            Task.Delay(1000); // I know it's kinda ugly, but so am I...

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
    }

    public class ProtocolRemoveLegacyAddons : IProtocolCommand {
        public string Command { get => "removelegacyaddons"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"removelegacyaddons\"!");

            // Check for K2EX add-on
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("KinectToVR")) ) {
                Logger.Info("Found K2EX add-on! Removing it...");
                InstallUtil.TryKillingConflictingProcesses();

                OpenVRUtil.ForceDisableDriver("KinectToVR");
                OpenVRUtil.RemoveDriversWithName("KinectToVR");

                InstallUtil.TryKillingConflictingProcesses();
                Logger.Info("Successfully removed K2EX add-on!");
                Util.ShowMessageBox("Successfully removed K2EX SteamVR add-on!", "Success");
            } else {
                Logger.Info("Couldn't find K2EX add-on!");
                Util.ShowMessageBox("No conflicting SteamVR add-ons were found!\nAmethyst can work properly.", "Success");
            }

            return true;
        }
    }

    public class ProtocolDisableOwotrack : IProtocolCommand {
        public string Command { get => "disableowotrack"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"disableowotrack\"!");

            // Check for owoTrack add-on
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("owoTrack")) ) {
                Logger.Info("Found owoTrack add-on! Disabling it...");
                InstallUtil.TryKillingConflictingProcesses();

                OpenVRUtil.ForceDisableDriver("owoTrack");

                Logger.Info("Successfully disabled owoTrack add-on!");
                Util.ShowMessageBox("Successfully disabled owoTrack add-on!", "Success");
            } else {
                Logger.Info("Couldn't find owoTrack add-on!");
                Util.ShowMessageBox("No conflicting SteamVR add-ons were found!\nAmethyst can work properly.", "Success");
            }

            return true;
        }
    }

    public class ProtocolOpenVr : IProtocolCommand {
        public string Command { get => "openvrpaths"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"openvrpaths\"!");
            Shell.OpenFolderAndSelectItem(Path.GetDirectoryName(OpenVRUtil.OpenVrPathsPath));
            return true;
        }
    }

    public class ProtocolLogs : IProtocolCommand {
        public string Command { get => "logs"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"logs\"!");
            Shell.OpenFolderAndSelectItem(Constants.AmethystLogsDirectory);
            return true;
        }
    }

    public class ProtocolCloseSteamVr : IProtocolCommand {
        public string Command { get => "closeconflictingapps"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"closeconflictingapps\"!");
            InstallUtil.TryKillingConflictingProcesses();
            return true;
        }
    }

    public class ProtocolAlvr : IProtocolCommand {
        public string Command { get => "alvr"; set { } }

        public bool Execute(string parameters) {
            App.Init();
            Logger.Info("Received protocol command \"alvr\"!");

            string alvrDir = OpenVRUtil.AlvrInstallPath;
            if ( alvrDir.Length == 0 ) {
                Logger.Info("Failed to locate ALVR install! Aborting...");
                Util.ShowMessageBox("Failed to locate ALVR install!", "Oops");
                return true;
            } else {
                Logger.Info($"ALVR install found at {alvrDir}");
            }

            // Check for existing ALVR add-on entries
            if ( Directory.Exists(OpenVRUtil.GetDriverPath("ALVR")) ) {
                Logger.Info("Found multiple ALVR add-ons! Removing...");
                OpenVRUtil.RemoveDriversWithName("ALVR");
            }

            Logger.Info(LogStrings.RegisteringAlvrDriver);

            string driverPath = Path.Combine(alvrDir, "ALVR");

            OpenVRUtil.RegisterSteamVrDriver(driverPath);
            InstallUtil.TryKillingConflictingProcesses();

            Util.ShowMessageBox("Successfully re-registered ALVR SteamVR add-on!", "Success");

            return true;
        }
    }

    public class ProtocolFuckYouNoelle : IProtocolCommand {
        public string Command { get => "alvinandthechipmunks"; set { } }

        public bool Execute(string parameters) {
            return new ProtocolAlvr().Execute(parameters);
        }
    }
    public class ProtocolOcusus : IProtocolCommand {
        public string Command { get => "ocusus"; set { } }

        public bool Execute(string parameters) {
            Process.Start("https://ocusus.com");
            return true;
        }
    }
}
