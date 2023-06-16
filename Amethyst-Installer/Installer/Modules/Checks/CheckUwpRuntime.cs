using amethyst_installer_gui.PInvoke;
using Microsoft.Win32;
using System;
using System.Linq;
using Windows.Management.Deployment;

namespace amethyst_installer_gui.Installer.Modules.Checks {
    public class CheckUwpRuntime : CheckBase {
        public override bool CheckShouldInstall(in Module module) {

            // We check if it's installed by the family name

            try {
                return new PackageManager().FindPackagesForUser(CurrentUser.GetCurrentlyLoggedInSid())
                    .Any(x => x.Id.Name is "Microsoft.VCLibs.140.00.UWPDesktop");
            } catch ( Exception ex ) {
                Logger.Fatal(Util.FormatException(ex));
            }

            return true;
        }
    }
}
