using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using LobbyClient;
using Microsoft.Win32;
using PlasmaShared.UnitSyncLib;
using SpringDownloader.Notifications;
using ZeroKLobby.MicroLobby;
using ZeroKLobby.Notifications;
using ZkData;
using ZkData.UnitSyncLib;

namespace ZeroKLobby
{
    static class Program
    {
        static readonly object configLock = new object();
        static NewVersionBar NewVersionBar;
        static Mutex mutex;
        public static AutoJoinManager AutoJoinManager;
        public static BattleBar BattleBar { get; private set; }
        public static BattleIconManager BattleIconManager { get; private set; }
        public static BrowserInterop BrowserInterop { get; private set; }
        public static bool CloseOnNext;
        public static Config Conf = new Config();
        public static ConnectBar ConnectBar { get; private set; }
        public static PlasmaDownloader.PlasmaDownloader Downloader { get; private set; }
        public static EngineConfigurator EngineConfigurator { get; set; }
        public static FriendManager FriendManager;
        public static MainWindow MainWindow { get; private set; }
        public static ModStore ModStore { get; private set; }
        public static NotifySection NotifySection { get { return MainWindow.NotifySection; } }
        public static SayCommandHandler SayCommandHandler { get; private set; }
        public static SelfUpdater SelfUpdater { get; set; }
        public static ServerImagesHandler ServerImages { get; private set; }
        public static SpringPaths SpringPaths { get; private set; }

        public static ZklSteamHandler SteamHandler { get; private set; }
        public static SpringScanner SpringScanner { get; private set; }
        public static SpringieServer SpringieServer = new SpringieServer();
        public static string[] StartupArgs;
        public static string StartupPath = Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath));
        public static bool IsSteamFolder { get; private set; }

        public static TasClient TasClient { get; private set; }
        public static ToolTipHandler ToolTip;
        public static VoteBar VoteBar { get; private set; }

        /// <summary>
        /// windows only: do we have admin token?
        /// </summary>
        public static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }


        internal static void LoadConfig()
        {
            var curConfPath = Path.Combine(StartupPath, Config.ConfigFileName);
            if (File.Exists(curConfPath)) Conf = Config.Load(curConfPath);
            else
            {
                Conf = Config.Load(Path.Combine(SpringPaths.GetMySpringDocPath(), Config.ConfigFileName));
                Conf.IsFirstRun = true; // treat import as a first run
            }
        }

        private static int GetNetVersionFromRegistry()
        {
            try
            {
                using (
                    RegistryKey ndpKey =
                        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                            .OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\"))
                {
                    int releaseKey = Convert.ToInt32(ndpKey.GetValue("Release"));
                    return releaseKey;
                }
            }
            catch
            {
                return 0;
            }
        }


        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                //Stopwatch stopWatch = new Stopwatch(); stopWatch.Start();

                Trace.Listeners.Add(new ConsoleTraceListener());
                Trace.Listeners.Add(new LogTraceListener());

                if (Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    var ver = GetNetVersionFromRegistry();
                    if (ver < 378675)
                    {
                        MessageBox.Show("Zero-K launcher needs Microsoft .NET framework 4.5.1\nPlease download and install it first",
                            "Program is unable to run", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                
                Directory.SetCurrentDirectory(StartupPath);

                IsSteamFolder = File.Exists(Path.Combine(StartupPath, "steamfolder.txt"));

                SelfUpdater = new SelfUpdater("Zero-K");

                StartupArgs = args;


                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!Debugger.IsAttached)
                {
                    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                    Thread.GetDomain().UnhandledException += UnhandledException;
                    Application.ThreadException += Application_ThreadException;
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                }

                //HttpWebRequest.DefaultCachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);



                Trace.TraceInformation("Starting with version {0}", SelfUpdater.CurrentVersion);

                WebRequest.DefaultWebProxy = null;
                ThreadPool.SetMaxThreads(500, 2000);
                ServicePointManager.Expect100Continue = false;
                if (Environment.OSVersion.Platform != PlatformID.Unix && !Conf.UseExternalBrowser) { Utils.SetIeCompatibility(); } //set to current IE version

                LoadConfig();


                var contentDir = !string.IsNullOrEmpty(Conf.DataFolder) ? Conf.DataFolder : StartupPath;
                if (!Directory.Exists(contentDir) || !SpringPaths.IsDirectoryWritable(contentDir) || contentDir.Contains("Local\\Apps"))
                {
                    var dc = new SelectWritableFolder() { SelectedPath = SpringPaths.GetMySpringDocPath() };
                    if (dc.ShowDialog() != DialogResult.OK) return;
                    contentDir = dc.SelectedPath;
                }
                if (Conf.DataFolder != StartupPath) Conf.DataFolder = contentDir;
                else Conf.DataFolder = null;

                if (!SpringPaths.IsDirectoryWritable(StartupPath) || StartupPath.Contains("Local\\Apps"))
                {
                    MessageBox.Show(
                        string.Format(
                            "Please use the newly created desktop icon to start Zero-K not this one.\r\nZero-K.exe will be moved to {0}",
                            contentDir), "Startup directory is not writable!");
                    var newTarget = Path.Combine(contentDir, "Zero-K.exe");
                    if (SelfUpdater.CheckForUpdate(newTarget, true))
                    {
                        Conf.Save(Path.Combine(contentDir, Config.ConfigFileName));
                        Process.Start(newTarget);
                        return;
                    }
                    MessageBox.Show("Move failed, please copy Zero-K.exe to a writable folder");
                    return;
                }




                SpringPaths = new SpringPaths(null, writableFolderOverride: contentDir);
                SpringPaths.MakeFolders();

                // speed up spring start
                SpringPaths.SpringVersionChanged += (sender, eventArgs) =>
                {
                    ZkData.Utils.StartAsync(
                        () =>
                        {
                            UnitSync unitSync = null;
                            try
                            {
                                unitSync = new UnitSync(SpringPaths); // initialize unitsync to avoid slowdowns when starting

                                if (unitSync.UnitsyncWritableFolder != SpringPaths.WritableDirectory)
                                {
                                    // unitsync created its cache in different folder than is used to start spring -> move it
                                    var fi = ArchiveCache.GetCacheFile(unitSync.UnitsyncWritableFolder);
                                    if (fi != null)
                                    {
                                        File.Copy(fi.FullName, Path.Combine(SpringPaths.WritableDirectory, fi.Name), true);
                                    }
                                }
                            }
                            finally
                            {
                                unitSync?.Dispose();
                            }
                        }); 
                    
                };
                

                
                SaveConfig();

                
                // write license files
                try {
                    var path = Program.SpringPaths.WritableDirectory;
                    var pathGPL = Utils.MakePath(path, "license_GPLv3");
                    string gpl = Encoding.UTF8.GetString(License.GPLv3);
                    if (!File.Exists(pathGPL))
                        File.WriteAllText(pathGPL, gpl);
                    var pathMIT = Utils.MakePath(path, "license_MIT");
                    string mit = Encoding.UTF8.GetString(License.MITlicense);
                    if (!File.Exists(pathMIT))
                        File.WriteAllText(pathMIT, mit);
                } catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }

                try
                {
                    if (!Debugger.IsAttached)
                    {
                        var wp = "";
                        foreach (var c in SpringPaths.WritableDirectory.Where(x => (x >= 'a' && x <= 'z') || (x >= 'A' && x <= 'Z'))) wp += c;
                        mutex = new Mutex(false, "ZeroKLobby" + wp);
                        if (!mutex.WaitOne(10000, false))
                        {
                            MessageBox.Show(
                                "Another copy of Zero-K lobby is still running" +
                                "\nMake sure the other lobby is closed (check task manager) before starting new one",
                                "There can be only one lobby running",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Stop);
                            return;
                        }
                    }
                }
                catch (AbandonedMutexException) { }

                if (Conf.IsFirstRun)
                {
                    DialogResult result = MessageBox.Show("Create a desktop icon for Zero-K?", "Zero-K", MessageBoxButtons.YesNo);
                    if (result == DialogResult.Yes)
                    {
                        Utils.CreateDesktopShortcut();
                    }
                    if (Environment.OSVersion.Platform != PlatformID.Unix)
                        Utils.RegisterProtocol();
                }

                FriendManager = new FriendManager();
                AutoJoinManager = new AutoJoinManager();
                EngineConfigurator = new EngineConfigurator(SpringPaths.WritableDirectory);

                SpringScanner = new SpringScanner(SpringPaths);
                SpringScanner.LocalResourceAdded += (s, e) => Trace.TraceInformation("New resource found: {0}", e.Item.InternalName);
                SpringScanner.LocalResourceRemoved += (s, e) => Trace.TraceInformation("Resource removed: {0}", e.Item.InternalName);
                if (Program.Conf.EnableUnitSyncPrompt && Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    SpringScanner.UploadUnitsyncData += MicroForms.UnitSyncUploadPrompt.SpringScanner_UploadUnitsyncData;
                    SpringScanner.RetryResourceCheck += MicroForms.UnitSyncRetryPrompt.SpringScanner_RetryGetResourceInfo;
                }

                SpringScanner.MapRegistered += (s, e) => Trace.TraceInformation("Map registered: {0}", e.MapName);
                SpringScanner.ModRegistered += (s, e) => Trace.TraceInformation("Mod registered: {0}", e.Data.Name);

                Downloader = new PlasmaDownloader.PlasmaDownloader(Conf, SpringScanner, SpringPaths); //rapid
                Downloader.DownloadAdded += (s, e) => Trace.TraceInformation("Download started: {0}", e.Data.Name);
                Downloader.GetAndSwitchEngine(ZkData.GlobalConst.DefaultEngineOverride ?? TasClient.ServerSpringVersion);

                var isLinux = Environment.OSVersion.Platform == PlatformID.Unix;
                TasClient = new TasClient(string.Format("ZK {0}{1}", SelfUpdater.CurrentVersion, isLinux ? " linux" : ""));

                SayCommandHandler = new SayCommandHandler(TasClient);

                ServerImages = new ServerImagesHandler(SpringPaths, TasClient);


                // log, for debugging
                TasClient.Connected += (s, e) => Trace.TraceInformation("TASC connected");
                TasClient.LoginAccepted += (s, e) =>
                    {
                        Trace.TraceInformation("TASC login accepted");
                        Trace.TraceInformation("Server is using Spring version {0}", TasClient.ServerSpringVersion);
                        if (Environment.OSVersion.Platform == PlatformID.Unix || Conf.UseExternalBrowser)
                            MainWindow.navigationControl.Path = "battles";
                    };

                TasClient.LoginDenied += (s, e) => Trace.TraceInformation("TASC login denied");
                TasClient.ChannelJoined += (s, e) => { Trace.TraceInformation("TASC channel joined: " + e.Name); };
                TasClient.ConnectionLost += (s, e) => Trace.TraceInformation("Connection lost");

                // special handling
                TasClient.PreviewSaid += (s, e) =>
                {
                    var tas = (TasClient)s;
                    User user = null;
                    if (e.Data.UserName != null)
                    {
                        tas.ExistingUsers.TryGetValue(e.Data.UserName, out user);
                        if ((user != null && user.BanMute) || Conf.IgnoredUsers.Contains(e.Data.UserName)) e.Cancel = true;
                    }
                };

                TasClient.SiteToLobbyCommandReceived += (eventArgs, o) =>
                    {
                        MainWindow.navigationControl.Path = o.Command;
                        MainWindow.PopupSelf();
                    };

                ConnectBar = new ConnectBar(TasClient);
                ModStore = new ModStore();
                ToolTip = new ToolTipHandler();
                BrowserInterop = new BrowserInterop(TasClient, Conf);
                BattleIconManager = new BattleIconManager();

                Application.AddMessageFilter(ToolTip);


                SteamHandler = new ZklSteamHandler(TasClient);
                SteamHandler.Connect();


                MainWindow = new MainWindow();

                Application.AddMessageFilter(new ScrollMessageFilter());


                if (Conf.StartMinimized) MainWindow.WindowState = FormWindowState.Minimized;
                else MainWindow.WindowState = FormWindowState.Normal;
                MainWindow.Size = new Size(Math.Min(SystemInformation.VirtualScreen.Width - 30, MainWindow.Width),
                                            Math.Min(SystemInformation.VirtualScreen.Height - 30, MainWindow.Height)); //in case user have less space than 1024x768

                BattleBar = new BattleBar();
                NewVersionBar = new NewVersionBar(SelfUpdater);
                VoteBar = new VoteBar();
                PwBar = new PwBar();

                //This make the size of every bar constant (only for height).
                //We wanted to make them constant because the bar get DPI-scaled twice/thrice/multiple-time (especially for reusable bar). 
                //Setting maximum height upon creation will hopefully make sure it is not DPI-scaled multiple time.
                var votebarSize = new Size(0, VoteBar.Height);
                // Reference: http://stackoverflow.com/questions/5314041/set-minimum-window-size-in-c-sharp-net
                var newversionbarSize = new Size(0, NewVersionBar.Height);
                var battlebarSize = new Size(0, BattleBar.Height);
                var connectbarSize = new Size(0, ConnectBar.Height);

                VoteBar.MinimumSize = votebarSize; //fix minimum size forever
                VoteBar.MaximumSize = votebarSize; //fix maximum size forever
                NewVersionBar.MinimumSize = newversionbarSize;
                NewVersionBar.MaximumSize = newversionbarSize;
                BattleBar.MinimumSize = battlebarSize;
                BattleBar.MaximumSize = battlebarSize;
                ConnectBar.MinimumSize = connectbarSize;
                ConnectBar.MaximumSize = connectbarSize;
                //End battlebar size hax

                if (!Debugger.IsAttached && !Conf.DisableAutoUpdate && !IsSteamFolder) Program.SelfUpdater.StartChecking();


                Application.Run(MainWindow);
                ShutDown();
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleException(ex, true);
                if (Debugger.IsAttached) Debugger.Break();
            }
            finally
            {
                ShutDown();
            }
            if (ErrorHandling.HasFatalException && !Program.CloseOnNext)
            {
                if (Debugger.IsAttached) Debugger.Break();
                Application.Restart();
            }
        }


        public static PwBar PwBar { get; private set; }

        internal static void SaveConfig()
        {
            lock (configLock)
            {
                Conf.Save(Path.Combine(StartupPath, Config.ConfigFileName));
            }
        }

        private static void FinalizeShutdown()
        {
            HistoryManager.FlushBuffer();
            Conf.IsFirstRun = false;

            if (Conf.DiscardPlayerName == true) { Conf.LobbyPlayerName = ""; }
            if (Conf.DiscardPassword == true) { Conf.LobbyPlayerPassword = ""; }

            SaveConfig();
            try
            {
                if (!Debugger.IsAttached) mutex.ReleaseMutex();
            }
            catch { }
            try
            {
                if (ToolTip != null) ToolTip.Dispose();
                if (Downloader != null) Downloader.Dispose();
                if (SpringScanner != null) SpringScanner.Dispose();
                if (SteamHandler != null) SteamHandler.Dispose();
            }
            catch { }
        }

        public static void ShutDown()
        {
            CloseOnNext = true;
            FinalizeShutdown();
            //Thread.Sleep(5000);
            Application.Exit();
        }

        public static void Restart()
        {
            CloseOnNext = true;
            FinalizeShutdown();
            //Thread.Sleep(5000);
            Application.Restart();
        }

        static void TasClientInvoker(TasClient.Invoker a)
        {
            if (!CloseOnNext) MainWindow.InvokeFunc(() => a());
        }

        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                ErrorHandling.HandleException(e.Exception, true);
                if (Debugger.IsAttached) Debugger.Break();
            }
            catch { }
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                ErrorHandling.HandleException((Exception)e.ExceptionObject, e.IsTerminating);
                if (Debugger.IsAttached) Debugger.Break();
            }
            catch { }
        }


        static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                ErrorHandling.HandleException((Exception)e.ExceptionObject, e.IsTerminating);
                if (Debugger.IsAttached) Debugger.Break();
            }
            catch { }
        }
    }
}
