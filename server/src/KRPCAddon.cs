using KRPC.Server;
using KRPC.Service.Scanner;
using KRPC.UI;
using KRPC.Utils;
using UnityEngine;

namespace KRPC
{
    /// <summary>
    /// Main KRPC addon. Contains the server instance and UI.
    /// </summary>
    [KSPAddonImproved (KSPAddonImproved.Startup.RealTime | KSPAddonImproved.Startup.Editor, false)]
    sealed public class KRPCAddon : MonoBehaviour
    {
        static KRPCServer server;
        static KRPCConfiguration config;
        static Texture textureOnline;
        static Texture textureOffline;

        ApplicationLauncherButton applauncherButton;
        MainWindow mainWindow;
        InfoWindow infoWindow;
        ClientConnectingDialog clientConnectingDialog;
        ClientDisconnectDialog clientDisconnectDialog;

        void Init ()
        {
            if (server != null)
                return;

            // Init server
            config = new KRPCConfiguration ("settings.cfg");
            config.Load ();
            server = new KRPCServer (
                config.Address, config.RPCPort, config.StreamPort,
                config.OneRPCPerUpdate, config.MaxTimePerUpdate, config.AdaptiveRateControl,
                config.BlockingRecv, config.RecvTimeout);
            KRPCServer.Context.SetServer (server);

            // Init services
            foreach (var service in Scanner.GetServices ()) {
                if (service.Value.Init != null)
                    service.Value.Init();
            }

            // Auto-start the server, if required
            if (config.AutoStartServer)
                StartServer ();
        }

        /// <summary>
        /// Wake the addon. Creates the server instance and UI.
        /// </summary>
        public void Awake ()
        {
            if (!ServicesChecker.OK)
                return;

            Init ();

            KRPCServer.Context.SetGameScene (KSPAddonImproved.CurrentGameScene.ToGameScene ());
            Logger.WriteLine ("Game scene switched to " + KRPCServer.Context.GameScene);

            GUILayoutExtensions.Init (gameObject);

            server.GetUniversalTime = Planetarium.GetUniversalTime;

            // Disconnect client dialog
            clientDisconnectDialog = gameObject.AddComponent<ClientDisconnectDialog> ();

            // Create info window
            infoWindow = gameObject.AddComponent<InfoWindow> ();
            infoWindow.Server = server;
            infoWindow.Closable = true;
            infoWindow.Visible = config.InfoWindowVisible;
            infoWindow.Position = config.InfoWindowPosition;

            // Create main window
            mainWindow = gameObject.AddComponent<MainWindow> ();
            mainWindow.Config = config;
            mainWindow.Server = server;
            mainWindow.Visible = config.MainWindowVisible;
            mainWindow.Position = config.MainWindowPosition;
            mainWindow.ClientDisconnectDialog = clientDisconnectDialog;
            mainWindow.InfoWindow = infoWindow;

            // Create new connection dialog
            clientConnectingDialog = gameObject.AddComponent<ClientConnectingDialog> ();

            // Main window events
            mainWindow.OnStartServerPressed += (s, e) => StartServer ();
            mainWindow.OnStopServerPressed += (s, e) => {
                server.Stop ();
                clientConnectingDialog.Close ();
            };
            mainWindow.OnHide += (s, e) => {
                config.Load ();
                config.MainWindowVisible = false;
                config.Save ();
            };
            mainWindow.OnShow += (s, e) => {
                config.Load ();
                config.MainWindowVisible = true;
                config.Save ();
            };
            mainWindow.OnMoved += (s, e) => {
                config.Load ();
                var window = s as MainWindow;
                config.MainWindowPosition = window.Position;
                config.Save ();
            };

            // Info window events
            infoWindow.OnHide += (s, e) => {
                config.Load ();
                config.InfoWindowVisible = false;
                config.Save ();
            };
            infoWindow.OnShow += (s, e) => {
                config.Load ();
                config.InfoWindowVisible = true;
                config.Save ();
            };
            infoWindow.OnMoved += (s, e) => {
                config.Load ();
                var window = s as InfoWindow;
                config.InfoWindowPosition = window.Position;
                config.Save ();
            };

            // Server events
            server.OnClientRequestingConnection += (s, e) => {
                if (config.AutoAcceptConnections)
                    e.Request.Allow ();
                else
                    clientConnectingDialog.OnClientRequestingConnection (s, e);
            };


            // Add a button to the applauncher
            mainWindow.Closable = true;
            textureOnline = GameDatabase.Instance.GetTexture ("kRPC/icons/applauncher-online", false);
            textureOffline = GameDatabase.Instance.GetTexture ("kRPC/icons/applauncher-offline", false);
            GameEvents.onGUIApplicationLauncherReady.Add (OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add (OnGUIApplicationLauncherDestroyed);
            server.OnStarted += (s, e) => {
                if (applauncherButton != null) {
                    applauncherButton.SetTexture (textureOnline);
                }
            };
            server.OnStopped += (s, e) => {
                if (applauncherButton != null) {
                    applauncherButton.SetTexture (textureOffline);
                }
            };
        }

        void OnGUIApplicationLauncherReady ()
        {
            applauncherButton = ApplicationLauncher.Instance.AddModApplication (
                () => mainWindow.Visible = !mainWindow.Visible,
                () => mainWindow.Visible = !mainWindow.Visible,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                server.Running ? textureOnline : textureOffline);
        }

        void OnGUIApplicationLauncherDestroyed ()
        {
            ApplicationLauncher.Instance.RemoveModApplication (applauncherButton);
            applauncherButton = null;
        }

        void StartServer ()
        {
            config.Load ();
            server.RPCPort = config.RPCPort;
            server.StreamPort = config.StreamPort;
            server.Address = config.Address;
            server.OneRPCPerUpdate = config.OneRPCPerUpdate;
            server.MaxTimePerUpdate = config.MaxTimePerUpdate;
            server.AdaptiveRateControl = config.AdaptiveRateControl;
            server.BlockingRecv = config.BlockingRecv;
            server.RecvTimeout = config.RecvTimeout;
            try {
                server.Start ();
            } catch (ServerException exn) {
                mainWindow.Errors.Add (exn.Message);
            }
        }

        /// <summary>
        /// Destroy the UI.
        /// </summary>
        public void OnDestroy ()
        {
            if (!ServicesChecker.OK)
                return;
            if (applauncherButton != null)
                OnGUIApplicationLauncherDestroyed ();
            GameEvents.onGUIApplicationLauncherReady.Remove (OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove (OnGUIApplicationLauncherDestroyed);
            Object.Destroy (mainWindow);
            Object.Destroy (clientConnectingDialog);
            GUILayoutExtensions.Destroy (gameObject);
        }

        /// <summary>
        /// Stop the server if running
        /// </summary>
        public void OnApplicationQuit ()
        {
            if (server.Running)
                server.Stop ();
        }

        /// <summary>
        /// Trigger server update
        /// </summary>
        public void FixedUpdate ()
        {
            if (!ServicesChecker.OK)
                return;
            if (server.Running)
                server.Update ();
        }
    }
}
