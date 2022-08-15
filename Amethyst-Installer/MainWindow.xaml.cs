using amethyst_installer_gui.Controls;
using amethyst_installer_gui.Installer;
using amethyst_installer_gui.Pages;
using amethyst_installer_gui.PInvoke;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace amethyst_installer_gui {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public Dictionary<InstallerState, IInstallerPage> Pages = new Dictionary<InstallerState, IInstallerPage>();
        // Basically an undo buffer ; max of 5 steps
        private List<InstallerState> pageStack = new List<InstallerState>(5);
        private int pageStackPointer = 0;

        public static AnalyticsData Analytics = new AnalyticsData();

        /// <summary>
        /// Returns the current instance of the <see cref="MainWindow"/>
        /// </summary>
        public static MainWindow Instance { get { return ( Application.Current.MainWindow as MainWindow ); } }
        /// <summary>
        /// The current locale string
        /// </summary>
        public static string LocaleCode = "en";
        // TODO: actually get the locale

        // Please read the shit
        private const int NextButtonCooldownMillis  = 1000;
        private long TimeSinceLastCooldown          = 0;
        private bool m_speedrunnerModeActive        = false;
        private DispatcherTimer m_dispatcherTimer   = new DispatcherTimer();
        private Stopwatch m_stopwatch               = new Stopwatch();
        private string m_currentTime                = "00:00.00";

        public MainWindow() {
            InitializeComponent();

            // Init pages
            Pages.Add(InstallerState.Welcome, new PageWelcome());
            Pages.Add(InstallerState.InstallOptions, new PageInstallOptions());
            Pages.Add(InstallerState.InstallDestination, new PageInstallDestination());
            Pages.Add(InstallerState.SystemRequirements, new PageSystemRequirements());
            Pages.Add(InstallerState.Downloading, new PageDownloading());
            Pages.Add(InstallerState.Installation, new PageInstallation());
            Pages.Add(InstallerState.Done, new PageDone());

            Pages.Add(InstallerState.Logs, new PageLogs());
            Pages.Add(InstallerState.EULA, new PageEULA());
            Pages.Add(InstallerState.Exception, new PageException());
            Pages.Add(InstallerState.Debug, new PageDebug());

            // Set default page to welcome
            SetPage(InstallerState.Welcome);

            // Fix corners on Win11
            DWM.SetWindowCorners(this, CornerPreference.Round);
            DWM.SetWindowAccentColor(this, WindowsColorHelpers.GetAccentColor());

            PrepareAnalytics();
        }

        private void PrepareAnalytics() {
            Analytics.Devices           = DeviceFlags.None;
            Analytics.HeadsetModel      = OpenVRUtil.HmdType;
            Analytics.TrackingUniverse  = OpenVRUtil.TrackingType;
            Analytics.ConnectionType    = OpenVRUtil.ConnectionType;
            Analytics.WindowsBuild      = "UNIMPLEMENTED"; // TODO: Windows build
        }

        #region Win UI 3 Window Functionality

        // Dragging
        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e) {
            if ( e.ChangedButton == MouseButton.Left )
                this.DragMove();
        }
        private void Titlebar_MouseRightUp(object sender, MouseButtonEventArgs e) {
            SystemCommands.ShowSystemMenu(this, PointToScreen(Mouse.GetPosition(this)));
        }

        // Titlebar buttons
        private void Close_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);
            Logger.Info($"User closed installer!");
            Close();
        }

        private void Minimise_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);
            WindowState = WindowState.Minimized;
        }

        #endregion

        #region Installer pages

        public IInstallerPage CurrentInstallerPage {
            get { return ( IInstallerPage ) GetValue(CurrentInstallerPageProperty); }
            set { SetValue(CurrentInstallerPageProperty, value); }
        }

        public static readonly DependencyProperty CurrentInstallerPageProperty =
            DependencyProperty.Register("CurrentInstallerPage", typeof(IInstallerPage), typeof(MainWindow), new PropertyMetadata(null, new PropertyChangedCallback(CurrentInstallerPageChanged)));


        private static void CurrentInstallerPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var mainWindowInstance = (d as MainWindow);
            var newPage = (IInstallerPage)e.NewValue;

            // Reset action button state to enabled on page load
            mainWindowInstance.ActionButtonPrimary.IsEnabled = true;
            mainWindowInstance.ActionButtonSecondary.IsEnabled = true;
            mainWindowInstance.ActionButtonTertiary.IsEnabled = true;

            // Set new page to view container
            mainWindowInstance.PageView.Content = newPage;
            newPage.OnFocus();
            mainWindowInstance.windowTitle.Text = newPage.GetTitle();

            // Reset if not logs
            if ( newPage.GetInstallerState() != InstallerState.Logs )
                mainWindowInstance.viewBntCount = 0;
        }

        public void SetPage(InstallerState destinatonTab) {

#pragma warning disable IDE0055

            // Update checkmarks in sidebar
            sidebar_welcome.State =         destinatonTab < InstallerState.Welcome ?                TaskState.Default : TaskState.Checkmark;
            sidebar_installOptions.State =  destinatonTab < InstallerState.InstallOptions ?         TaskState.Default : TaskState.Checkmark;
            sidebar_location.State =        destinatonTab < InstallerState.InstallDestination ?     TaskState.Default : TaskState.Checkmark;
            sidebar_sysreq.State =          destinatonTab < InstallerState.SystemRequirements ?     TaskState.Default : TaskState.Checkmark;
            sidebar_download.State =        destinatonTab < InstallerState.Downloading ?            TaskState.Default : TaskState.Checkmark;
            sidebar_install.State =         destinatonTab < InstallerState.Installation ?           TaskState.Default : TaskState.Checkmark;
            sidebar_done.State =            destinatonTab < InstallerState.Done ?                   TaskState.Default : TaskState.Checkmark;

#pragma warning restore IDE0055

            CurrentInstallerPage = Pages[destinatonTab];
            Logger.Info($"Changing installer page to {destinatonTab}");
            CurrentInstallerPage.OnSelected();

            // Reset the stack
            pageStack.Clear();
            pageStackPointer = 0;
            pageStack.Add(destinatonTab);

        }

        // Used to take flow from current to some other page
        public void OverridePage(InstallerState target) {
            if ( pageStack.Count > 0 && pageStack[pageStackPointer] == InstallerState.Logs )
                return;
            pageStack.Add(target);
            Logger.Info($"Overriding view to page {target}...");
            pageStackPointer++;
            CurrentInstallerPage = Pages[target];
        }
        // Goes down the page stack
        public void GoToLastPage() {
            pageStackPointer--;
            CurrentInstallerPage = Pages[pageStack[pageStackPointer]];
            Logger.Info($"Changing view to previous page {pageStack[pageStackPointer]}...");
        }

        private void ActionButtonPrimary_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);
#if !DEBUG
            if ( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TimeSinceLastCooldown < NextButtonCooldownMillis ) {
                if ( !m_speedrunnerModeActive ) {
                    m_speedrunnerModeActive = true;

                    // Show prompt
                    Util.ShowMessageBox(Properties.Resources.Speedrunner_Description, Properties.Resources.Speedrunner_Title, MessageBoxButton.OK);

                    // In a perfect world, we would play Dream music here, but unfortunately, licensing is a thing, so oh no!

                    // Speedrun timer
                    m_dispatcherTimer.Tick += new EventHandler(speedunTimer_Tick);
                    m_dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 10);
                    m_stopwatch.Start();
                    m_dispatcherTimer.Start();
                    speedrunTimer.Visibility = Visibility.Visible;

                    // TODO: On install complete victory royale ??
                }
            } else {
#endif
            CurrentInstallerPage.OnButtonPrimary(sender, e);
                TimeSinceLastCooldown = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if !DEBUG
        }
#endif
    }

        private void ActionButtonSecondary_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);
            CurrentInstallerPage.OnButtonSecondary(sender, e);
        }
        private void ActionButtonTertiary_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);
            CurrentInstallerPage.OnButtonTertiary(sender, e);
        }

#endregion

        int viewBntCount = 0;
        readonly string[] altLogsBtnStrings = new string[]
        {
            "Chungus Bungus",
            "Among Us",
            "LOGS LOGS LOGS LOGS LOGS LOGS LOGS LOGS LOGS LOGS LOGS LOGS",
        };
        bool altLogsBtnTxtActive = false;

        private void viewLogsBtn_Click(object sender, RoutedEventArgs e) {
            Util.HandleKeyboardFocus(e);

            if ( altLogsBtnTxtActive == false && viewBntCount > 2 && pageStack[pageStackPointer] == InstallerState.Logs ) {
                altLogsBtnTxtActive = true;
                viewLogsBtn.Content = altLogsBtnStrings[new Random().Next(0, altLogsBtnStrings.Length)];
            }
            viewBntCount++;
            OverridePage(InstallerState.Logs);
        }

        // Clear focus of the currently tabbed item, mimicing WinUI3's behavior
        private void ContentRoot_MouseDown(object sender, MouseButtonEventArgs e) {
            Keyboard.ClearFocus();
        }

        private void Window_KeyUp(object sender, KeyEventArgs e) {
#if DEBUG
            if ( e.Key == Key.F12 ) {
                OverridePage(InstallerState.Debug);
            }
#endif
        }

        void speedunTimer_Tick(object sender, EventArgs e) {
            if ( m_stopwatch.IsRunning ) {
                TimeSpan ts = m_stopwatch.Elapsed;
                m_currentTime = string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                speedrunTimer.Content = m_currentTime;
            }
        }

        public void StopSpeedrunTimer() {
            if ( m_stopwatch.IsRunning && m_speedrunnerModeActive ) {
                m_stopwatch.Stop();
            }
        }
    }
}
