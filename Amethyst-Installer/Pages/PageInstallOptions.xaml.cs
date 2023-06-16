using amethyst_installer_gui.Controls;
using amethyst_installer_gui.Installer;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace amethyst_installer_gui.Pages {
    /// <summary>
    /// Interaction logic for PageInstallOptions.xaml
    /// </summary>
    public partial class PageInstallOptions : UserControl, IInstallerPage {

        private List<InstallableItem> installableItemControls;
        private List<Module> m_processedModules = new List<Module>();
        private Module m_currentModule;

        private long m_currentModuleDownloadSize = 0;
        private long m_currentModuleInstallSize = 0;

        private long m_totalDownloadSize = 0;
        private long m_totalInstallSize = 0;

        public PageInstallOptions() {
            InitializeComponent();
            installableItemControls = new List<InstallableItem>();
        }

        public InstallerState GetInstallerState() {
            return InstallerState.InstallOptions;
        }

        public string GetTitle() {
            return Localisation.Manager.Page_InstallOptions_Title;
        }

        private void ActionButtonPrimary_Click(object sender, RoutedEventArgs e) {

            Util.HandleKeyboardFocus(e);
            if ( !MainWindow.HandleSpeedrun() )
                return;

            // Prepare other data for the next page
            PageSystemRequirements.FreeDriveSpace = new DriveInfo("C").AvailableFreeSpace;

            InstallerStateManager.ModulesToInstall.Clear();
            List<Module> modulesPostBuffer = new();

            foreach (var t in installableItemControls)
            {
                var module = ( Module ) t.Tag;

                // Directly read the checkbox because when toggling the chexbox rather than clicking the control directly the
                // "Checked" property's state is deferred till after this method is executed, however the "itemCheckbox.IsChecked"
                // property is reliable for this page's purposes
                bool isChecked = t.Disabled || (t.itemCheckbox?.IsChecked ?? false);

                // Go through dependencies
                foreach (Module thisModule in module.Depends.Select(t1 => InstallerStateManager.API_Response
                             .Modules[InstallerStateManager.ModuleIdLUT[t1]]).Where(_ => isChecked))
                {
                    // For dependency in X
                    Logger.Info($"Queueing dependency \"{thisModule.Id}\"...");
                    if ( !InstallerStateManager.ModulesToInstall.Contains(thisModule) && 
                         InstallerStateManager.ShouldInstallModule(thisModule) ) {
                        InstallerStateManager.ModulesToInstall.Add(thisModule);
                    }
                }

                if ( !isChecked ) continue;

                Logger.Info($"Queueing module \"{module.Id}\"...");
                modulesPostBuffer.Add(module);
            }

            // Merge the dependencies and modules lists together, so that dependencies are earlier than modules.
            // This should resolve dependency chain issues where a module installs out of order
            for ( int i = 0; i < InstallerStateManager.ModulesToInstall.Count; i++ ) {
                foreach (Module _ in modulesPostBuffer
                             .Select(t => new { t, deps = InstallerStateManager.ModulesToInstall[i] })
                             .Select(@t1 => new { @t1, mod = @t1.t })
                             .Where(@t1 => @t1.@t1.deps.Id == @t1.mod.Id)
                             .Select(@t1 => @t1.@t1.deps) )
                {
                    InstallerStateManager.ModulesToInstall.RemoveAt(i);
                    i--;
                }
            }
            // Add the list of modules which depend on other modules to the back of the modules to install vector
            InstallerStateManager.ModulesToInstall.AddRange(modulesPostBuffer);

            installOptionsContainer.Children.Clear();
            installableItemControls.Clear();

            PageSystemRequirements.RequiredStorage = m_totalDownloadSize + m_totalInstallSize;

            // Advance to next page
            SoundPlayer.PlaySound(SoundEffect.MoveNext);
            MainWindow.Instance.SetPage(InstallerState.SystemRequirements);
        }

        public void OnSelected() {

            for (int i = 0; i < InstallerStateManager.API_Response.Modules.Count; i++ ) {

                var currentModule = InstallerStateManager.API_Response.Modules[i];
                if ( !currentModule.Visible ) {
                    continue;
                }

                var currentControl = new InstallableItem();
                if ( InstallerStateManager.ModuleStrings.ContainsKey(currentModule.Id) ) {
                    currentControl.Title = InstallerStateManager.ModuleStrings[currentModule.Id].Title;
                    currentControl.Description = InstallerStateManager.ModuleStrings[currentModule.Id].Summary;
                } else {
                    currentControl.Title = currentModule.Id;
                    currentControl.Description = "Lorem ipsum dolor sit amet constecteur adispiling.";
                }
                currentControl.Checked = ShouldAutoSelectModule(ref currentModule);
                currentControl.Disabled = currentModule.Required;
                currentControl.Margin = new Thickness(0, 0, 0, 8);
                currentControl.OnMouseClickReleased += InstallOptionMouseReleaseHandler;
                currentControl.OnToggled += InstallOptionCheckToggledHandler;
                currentControl.Tag = currentModule;
                currentControl.Focusable = false;

                installableItemControls.Add(currentControl);
                installOptionsContainer.Children.Add(currentControl);
            }

            // Select the first item, Amethyst
            installableItemControls[0].Click();
        }

        private bool ShouldAutoSelectModule(ref Module module) {
            if ( module.Required )
                return true;

            switch ( module.Id ) {
                case "kinect-v1-sdk":
                    return KinectUtil.IsKinectV1Present();
                case "kinect-v2-sdk":
                    return KinectUtil.IsKinectV2Present();
            }

            return false;
        }

        private void InstallOptionCheckToggledHandler(object sender, RoutedEventArgs e) {
            InstallableItem selectedItem = sender as InstallableItem;
            m_currentModule = selectedItem.Tag as Module;

            // Auto-select dependencies
            bool doSelect = selectedItem?.itemCheckbox?.IsChecked ?? false;
            if ( doSelect ) {
                for ( int i = 0; i < installableItemControls.Count; i++ ) {
                    for ( int j = 0; j < m_currentModule.Depends.Count; j++ ) {
                        if ( ( ( Module ) installableItemControls[i].Tag ).Id == m_currentModule.Depends[j] ) {
                            installableItemControls[i].itemCheckbox.IsChecked = doSelect;
                        }
                    }
                }
            }

            // Update right hand side
            CalculateInstallSize(m_currentModule);

            downloadSize.Content = Util.SizeSuffix(m_currentModuleDownloadSize);
            installSize.Content = Util.SizeSuffix(m_currentModuleInstallSize);

            totalDownloadSize.Content = Util.SizeSuffix(m_totalDownloadSize);
            totalInstallSize.Content = Util.SizeSuffix(m_totalInstallSize);
        }

        private void InstallOptionMouseReleaseHandler(object sender, MouseButtonEventArgs e) {

            if ( e != null )
                SoundPlayer.PlaySound(SoundEffect.Invoke);
            InstallableItem selectedItem = sender as InstallableItem;
            m_currentModule = selectedItem.Tag as Module;

            for ( int i = 0; i < installableItemControls.Count; i++ ) {
                if ( installableItemControls[i] != selectedItem ) {
                    // Handle background
                    installableItemControls[i].Background = new SolidColorBrush(Colors.Transparent);
                } else {
                    // Auto-select dependencies
                    for ( int j = 0; j < m_currentModule.Depends.Count; j++ ) {
                        if ( ( ( Module ) installableItemControls[i].Tag ).Id == m_currentModule.Depends[j] ) {
                            installableItemControls[i].Checked = selectedItem.Checked;
                        }
                    }
                }
            }

            // Update right hand side
            CalculateInstallSize(m_currentModule);

            if ( InstallerStateManager.ModuleStrings.ContainsKey(m_currentModule.Id) ) {
                fullTitle.Text = InstallerStateManager.ModuleStrings[m_currentModule.Id].Title;
                fullDescription.Text = InstallerStateManager.ModuleStrings[m_currentModule.Id].Description;
            } else {
                fullTitle.Text = m_currentModule.Id;
                fullDescription.Text = "Lorem ipsum dolor sit amet constecteur adispiling.";
            }

            downloadSize.Content = Util.SizeSuffix(m_currentModuleDownloadSize);
            installSize.Content = Util.SizeSuffix(m_currentModuleInstallSize);

            totalDownloadSize.Content = Util.SizeSuffix(m_totalDownloadSize);
            totalInstallSize.Content = Util.SizeSuffix(m_totalInstallSize);
        }

        private void CalculateInstallSize(Module currentModule) {

            m_currentModuleDownloadSize = 0;
            m_currentModuleInstallSize  = 0;
            m_totalDownloadSize         = 0;
            m_totalInstallSize          = 0;

            m_processedModules.Clear();

            for ( int i = 0; i < installableItemControls.Count; i++ ) {

                var module = ( Module ) installableItemControls[i].Tag;

                // Directly read the checkbox because when toggling the chexbox rather than clicking the control directly the
                // "Checked" property's state is deferred till after this method is executed, however the "itemCheckbox.IsChecked"
                // property is reliable for this page's purposes
                bool isChecked = installableItemControls[i].Disabled ?
                    true : (installableItemControls[i].itemCheckbox?.IsChecked ?? false);

                // Collect the current root module's file sizes
                if ( isChecked && !m_processedModules.Contains(module) ) {
                        m_processedModules.Add(module);
                }

                if ( module == currentModule ) {
                    m_currentModuleDownloadSize += module.DownloadSize;
                    m_currentModuleInstallSize += module.FileSize;
                }

                // Collect the dependency module's file sizes
                for ( int j = 0; j < module.Depends.Count; j++ ) {

                    var thisModule = InstallerStateManager.API_Response.Modules[InstallerStateManager.ModuleIdLUT[module.Depends[j]]];

                    if ( isChecked && !m_processedModules.Contains(thisModule) ) {
                        m_processedModules.Add(thisModule);
                    }

                    if ( module == currentModule ) {
                        m_currentModuleDownloadSize += thisModule.DownloadSize;
                        m_currentModuleInstallSize += thisModule.FileSize;
                    }
                }
            }

            for (int i = 0; i < m_processedModules.Count; i++ ) {
                m_totalDownloadSize += m_processedModules[i].DownloadSize;
                m_totalInstallSize += m_processedModules[i].FileSize;
            }
        }

        // Force only the first button to have focus
        public void OnFocus() {
            MainWindow.Instance.ActionButtonPrimary.Visibility = Visibility.Visible;
            MainWindow.Instance.ActionButtonPrimary.Content = Localisation.Manager.Installer_Action_Next;
            MainWindow.Instance.ActionButtonSecondary.Visibility = Visibility.Hidden;
            MainWindow.Instance.ActionButtonTertiary.Visibility = Visibility.Hidden;

            MainWindow.Instance.SetSidebarHidden(false);
            MainWindow.Instance.SetButtonsHidden(true);
        }

        public void OnButtonPrimary(object sender, RoutedEventArgs e) { }
        public void OnButtonSecondary(object sender, RoutedEventArgs e) { }
        public void OnButtonTertiary(object sender, RoutedEventArgs e) { }
    }
}
