using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Xml.Linq;
using WinForms = System.Windows.Forms;

namespace Client
{
    /// <summary>
    /// Logica di interazione per Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        private ClientConnection client;
        private CancellationTokenSource cts = new CancellationTokenSource();
        private FileSystemWatcher Watcher;
        private XmlManager XMLInstance;
        private System.Windows.Forms.NotifyIcon MyNotifyIcon;
        private System.Windows.Forms.ContextMenu menu_tray;

        private Dictionary<string, List<VersionInfo>> remoteVersionMap = new Dictionary<string, List<VersionInfo>>();
        private List<string> deletedFilesList = new List<string>();

        static string lastXmlDigest = "";

        private Task xmlGenerationTask;
        static bool userRequestShutdown = false;
        // TIMER
        static System.Timers.Timer SynchTimer;
        
        readonly object _fswLocker = new object();
        private Queue<FSWEventListElement> fswEventQueue = new Queue<FSWEventListElement>();
        Thread fswEventthread;
        // Posso usare semplicemente un bool visto che non faccio operazioni di 
        // contronto e modifica https://msdn.microsoft.com/en-us/library/aa691278%28VS.71%29.aspx
        private static volatile bool statusInSynch;

        // uso questo lock per bloccare il timer che richiama la synch
        readonly object synchTimerLock = new object();

        private string authToken = Constants.DefaultAuthToken;

        #region COSTRUTTORE - USCITA
        public Window1()
        {
            InitializeComponent();

            ConfigureSystemTray();

            //foreach (TabItem item in TABControl.Items)
            //{
            //    item.Visibility = Visibility.Collapsed;
            //}

            //((TabItem)TABControl.Items.GetItemAt(0)).Visibility = Visibility.Collapsed;
            //((TabItem)TABControl.Items.GetItemAt(1)).Visibility = Visibility.Collapsed;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            client = new ClientConnection();
            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|          AVVIO CLIENT              |");
            Logger.log("--------------------------------------");
            Logger.log("");

            ConfigureWatcher();
            ConfigureTimer();

            // Carico le impostazioni
            TXTpathCartella.Text = Settings.SynchPath;
            NUDTimerValue.Value = Settings.TimerFrequency / 1000; // Converto i ms in secondi
            TXTServerIP.Text = Settings.ServerIP;
            TXTServerPort.Text = Settings.ServerPort.ToString();

            // Genero l'xml in un task che raccoglierò successivamente
            Logger.Info("Lancio xml");
            xmlGenerationTask = Task.Run(() =>
            {
                XMLInstance = new XmlManager(new DirectoryInfo(Settings.SynchPath));
            });

            // Avvio timer e watcher
            StartWatcher();
            StopTimer();//StartTimer();

            // Vado a leggere le credenziali salvate
            if (File.Exists("credenziali.dat") == true)
            {
                
                using (StreamReader sr = new StreamReader("credenziali.dat"))
                {
                    string savedUsername = sr.ReadLine();
                    string savedPwd = sr.ReadLine();

                    TXTUsernameInserito.Text = savedUsername;
                    TXTPasswordInserita.Text = savedPwd;
                }

                ChkRicorda.IsChecked = true;
            }


        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if(userRequestShutdown == false)
            {
                e.Cancel = true;
                this.WindowState = WindowState.Minimized;
            }
        
        }

        private void applicationShutDown()
        {
            MyNotifyIcon.Visible = false;
            MyNotifyIcon.Dispose();

            cts.Cancel();

            // Interrompo il thread per il fsw
            lock (_fswLocker)
            {
                // Metto in coda
                fswEventQueue.Enqueue(null);
                // Sveglio il thread
                Monitor.Pulse(_fswLocker);
            }

            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|        CHIUSURA CLIENT             |");
            Logger.log("--------------------------------------");
            Logger.log("");

            userRequestShutdown = true;
            this.Close();
        }

        #endregion

        #region SYSTEM TRAY
        private void ConfigureSystemTray()
        {
            MyNotifyIcon = new System.Windows.Forms.NotifyIcon();
            MyNotifyIcon.Icon = new System.Drawing.Icon(Constants.IcoPath);

            MyNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(MyNotifyIcon_MouseDoubleClick);
            //MyNotifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(MyNotifyIcon_MouseClick);
            //MyNotifyIcon.Click += new System.Windows.Forms.MouseEventHandler(NotifyIcon_NotificationAreaClick);

            //gestione menu tray
            menu_tray = new System.Windows.Forms.ContextMenu();
            menu_tray.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Apri", new System.EventHandler(Show_Click)));
            menu_tray.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Chiudi applicazione", new System.EventHandler(Exit_Click)));
            MyNotifyIcon.ContextMenu = menu_tray;


            MyNotifyIcon.Visible = true;
            statusInSynch = false;
        }

        protected void Exit_Click(Object sender, System.EventArgs e)
        {
            applicationShutDown();
        }

        protected void Show_Click(Object sender, System.EventArgs e)
        {
            this.WindowState = WindowState.Normal;
            //this.Show();
        }

        void MyNotifyIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            //this.Show();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.ShowInTaskbar = false;
                //MyNotifyIcon.BalloonTipTitle = "App status";
                //MyNotifyIcon.BalloonTipText = "The app is minimized in the System Tray";
                //yNotifyIcon.ShowBalloonTip(2000, "App status", "The app is minimized in the System Tray", ToolTipIcon.Info);
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this.ShowInTaskbar = true;
                //MyNotifyIcon.BalloonTipTitle = "App status";
                //MyNotifyIcon.BalloonTipText = "The app is currently running";
                //MyNotifyIcon.ShowBalloonTip(2000, "App status", "The app is currently running" , ToolTipIcon.Info);
            }

            base.OnStateChanged(e);
        }

        //protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        //{
        //    e.Cancel = false;
        //    //this.WindowState = WindowState.Minimized;

        //    base.OnClosing(e);
        //}
        #endregion                

        #region TIMER
        private void ConfigureTimer()
        {
            SynchTimer = new System.Timers.Timer()
            {
                Interval = Settings.TimerFrequency,
                AutoReset = true,
                Enabled = false
            };

            SynchTimer.Elapsed += new ElapsedEventHandler(SynchTimerTick);
            StopTimer();
        }

        private void StartTimer()
        {
            SynchTimer.Enabled = true;
            SynchTimer.Start();
        }

        private void StopTimer()
        {
            SynchTimer.Enabled = false;
            SynchTimer.Stop();
        }

        private void SynchTimerTick(object sender, ElapsedEventArgs e)
        {
            try
            {
                // chiamo la synch usando il thread pool
                ThreadPool.QueueUserWorkItem(Synch);

                //necessario per far si che il thread lanciato per la gestione dell'evento possa
                //interagire con il thread principale che gestisce l'interfaccia grafica
                Dispatcher.Invoke(new Action(() => {
                    printXmlToTreeView();
                }), System.Windows.Threading.DispatcherPriority.Background, cts.Token);

            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }

        }

        private void BtnStopTimer_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        private void BtnStartTimer_Click(object sender, RoutedEventArgs e)
        {
            StartTimer();
        }
        #endregion

        #region XML TO TREEVIEW
        private void printXmlToTreeView()
        {
            // Controllo di dover aggiornare la TRW
            string currentXmlDigest = XMLInstance.XMLDigest();
            if (currentXmlDigest.CompareTo(lastXmlDigest) == 0)
            {
                // XML uguali, ritorno
                return;
            }
            else
            {
                lastXmlDigest = currentXmlDigest;
            }

            XElement root = XMLInstance.GetRoot();
            TRWFolder.Items.Clear();

            StopTimer();

            TRWFolder.Items.Add(xmlToTreeViewDirectory(root));

            //espando il primo elemento (root)
            ((TreeViewItem)TRWFolder.Items.GetItemAt(0)).IsExpanded = true;

            StartTimer();
        }

        private TreeViewItem xmlToTreeViewDirectory(XElement el)
        {
            TreeViewItem ret = new TreeViewItem();
            // Setto il nome della foglia nell'albero
            ret.Header = el.Attribute(XmlManager.DirectoryAttributeName).Value;

            // Visualizzo le cartelle ed effettuo la ricorsione
            foreach (XElement item in el.Elements(XmlManager.DirectoryElementName))
            {
                ret.Items.Add(xmlToTreeViewDirectory(item));
            }

            // Visualizzo i file
            foreach (XElement item in el.Elements(XmlManager.FileElementName))
            {
                // Normalizzo la dimensione del file
                long size = Convert.ToInt64(item.Attribute(XmlManager.FileAttributeSize).Value);
                string val = item.Attribute(XmlManager.FileAttributeName).Value + " (" + Utilis.NormalizeSize(size) + ")";
                ret.Items.Add(val);
            }

            return ret;
        }
        #endregion

        #region FILESYSTEM WATCHER
        private void ConfigureWatcher()
        {
            Watcher = new FileSystemWatcher()
            {
                Path = Settings.SynchPath,

                //NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                //NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime | NotifyFilters.Attributes,
                Filter = "*.*",
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024 //max possible buffer size
            };
            Watcher.EnableRaisingEvents = false;

            Watcher.Changed += new FileSystemEventHandler(OnChanged);
            Watcher.Created += new FileSystemEventHandler(OnChanged);
            Watcher.Deleted += new FileSystemEventHandler(OnChanged);
            Watcher.Renamed += new RenamedEventHandler(OnRenamed);
            
            fswEventthread = new Thread(fswEventConsumer);
            fswEventthread.Start();
        }

        private void StartWatcher()
        {
            Watcher.EnableRaisingEvents = true;
        }

        private void StopWatcher()
        {
            Watcher.EnableRaisingEvents = false;
        }

        /// <summary>
        /// Evento lanciato quando un file/directory viene
        /// -Modificato
        /// -Cancellato
        /// -Creato
        /// </summary>
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            
            try
            {

                lock (_fswLocker)
                {
                    // Metto in coda
                    fswEventQueue.Enqueue(new FSWEventListElement() { absPath = e.FullPath, ChangeType = e.ChangeType });
                    // Sveglio il thread
                    Monitor.Pulse(_fswLocker);
                }

                //if (e.ChangeType == WatcherChangeTypes.Changed)
                //{
                //    // IGNORO le notifiche di tipo change su directory
                //    if (Utilis.IsDirectory(e.FullPath))
                //        return;
                //
                //    Logger.Debug("Changed FILE " + e.FullPath);
                //    //FileAttributeHelper fileAttr = new FileAttributeHelper(e.FullPath);
                //    //XMLInstance.RefreshFile(fileAttr);
                //    //Thread.Sleep(Utilis.rndGen.Next(1, 5));
                //
                //}
                //else if (e.ChangeType == WatcherChangeTypes.Created)
                //{
                //    if (Utilis.IsDirectory(e.FullPath))
                //    {
                //        Logger.Debug("Created DIR " + e.FullPath);
                //        //XMLInstance.CreateDirectory(e.FullPath);
                //        //Thread.Sleep(Utilis.rndGen.Next(1, 5));
                //    }
                //    else
                //    {
                //        Logger.Debug("Created FILE " + e.FullPath);
                //        //FileAttributeHelper fileAttr = new FileAttributeHelper(e.FullPath);
                //        //XMLInstance.CreateFile(fileAttr);
                //        //Thread.Sleep(Utilis.rndGen.Next(1, 5));
                //
                //        // Se un file è stato creato dovrei rimuoverlo dalla lista
                //        //deletedFilesList.Remove(Utilis.AbsToRelativePath(e.FullPath, Settings.SynchPath));
                //    }
                //   
                //}
                //else if (e.ChangeType == WatcherChangeTypes.Deleted)
                //{
                //    // Non posso sapere se è file o directory
                //    Logger.Info("DELETED  " + e.FullPath);
                //    //XMLInstance.DeleteElement(e.FullPath, deletedFilesList);
                //    //Thread.Sleep(Utilis.rndGen.Next(1, 5));
                //}


            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }            
        }

        /// <summary>
        /// Evento lanciato quando viene rinominato un oggetto
        /// </summary>
        private void OnRenamed(object source, RenamedEventArgs e)
        {
            try
            {
                Logger.Debug(e.ChangeType + " " + e.OldFullPath + " -> " + e.FullPath);

                if (Utilis.IsDirectory(e.FullPath))
                {   // Directory
                    XMLInstance.RenameDirectory(e.OldName, e.Name);
                }
                else
                {   // File
                    XMLInstance.RenameFile(e.OldName, e.Name);
                }

                //XMLInstance.SaveToFile(Constants.XmlSavePath + @"\x2.xml");
                

            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
        }
        
        /// <summary>
        /// Thread per gestire gli eventi del client
        /// </summary>
        private void fswEventConsumer()
        {
            try
            {
                while (true)                        
                {
                    FSWEventListElement element;
                    lock (_fswLocker)
                    {
                        while (fswEventQueue.Count == 0 || statusInSynch == true)
                            Monitor.Wait(_fswLocker);

                        // Estraggo l'elemento
                        element = fswEventQueue.Dequeue();

                        // Se ricevo null significa che devo chiudere
                        if (element == null)
                            return;           
                    }

                    // Consumo l'elemento
                    switch (element.ChangeType)
                    {
                        case WatcherChangeTypes.Changed:
                            if (!Utilis.IsDirectory(element.absPath))
                            {
                                Logger.Debug("Changed FILE" + element.absPath);
                                FileAttributeHelper fileAttr = new FileAttributeHelper(element.absPath);
                                XMLInstance.RefreshFile(fileAttr);
                            }
                            break;

                        case WatcherChangeTypes.Created:
                            if (Utilis.IsDirectory(element.absPath))
                            {
                                Logger.Debug("Created DIR " + element.absPath);
                                XMLInstance.CreateDirectory(element.absPath);
                            }
                            else
                            {
                                Logger.Debug("Created FILE " + element.absPath);
                                FileAttributeHelper fileAttr = new FileAttributeHelper(element.absPath);
                                XMLInstance.CreateFile(fileAttr);

                                //Se un file è stato creato dovrei rimuoverlo dalla lista
                                deletedFilesList.Remove(Utilis.AbsToRelativePath(element.absPath, Settings.SynchPath));
                            }
                            break;

                        case WatcherChangeTypes.Deleted:
                            Logger.Debug("Deleted " + element.absPath);
                            XMLInstance.DeleteElement(element.absPath, deletedFilesList);
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
        }
    
        #endregion

        #region TAB CHANGE

        /// <summary>
        /// Penso serva a tornare nel main
        /// </summary>
        private void BTNRestoreToMain_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 2;
        }


        private void GotoLogin()
        {
            TABControl.SelectedIndex = (int)TabIndexEnum.Login;
        }

        private void GotoRegistration()
        {
            TABControl.SelectedIndex = (int)TabIndexEnum.Registration;
        }

        private void GotoMain()
        {
            TABControl.SelectedIndex = (int)TabIndexEnum.Main;
        }

        private void GotoRestore()
        {
            TABControl.SelectedIndex = (int)TabIndexEnum.Restore;
        }
        
        private void GotoSettings()
        {
            TABControl.SelectedIndex = (int)TabIndexEnum.Settings;
        }

        #endregion

        #region ACCOUNT MANAGE
        /// <summary>
        /// funzione chiamata al premere del link "Non possiedi un account? Registrati", crea il nuovo account e redirezione su MainWindow
        /// </summary>
        private void AddAccount(object sender, RoutedEventArgs e)
        {
            //monto la scheda Registrazione
            TABControl.SelectedIndex = 1;
        }

        /// <summary>
        /// funzione chiamata al premere del pulsante Login, controlla i dati inseriti e permette l'accesso a Window1
        /// (sia per la checkLogin sia per la sendRegistration)
        /// </summary>
        private async void BTNLogin_Clicked(object sender, RoutedEventArgs e)
        {
            string username = "";
            string pwd = "";

            #region Validazione input
            try
            {
                username = TXTUsernameInserito.Text;
                pwd = TXTPasswordInserita.Text;

                int usnLen = username.Length;
                if (usnLen < Constants.MinUsernameLength || usnLen > Constants.MaxUsernameLength)
                {
                    TABControl.SelectedIndex = 0;
                    Logger.Error("lunghezza username non valida" + username);
                    System.Windows.MessageBox.Show("lunghezza username non valida" + username);
                    return;
                }

                int pwdLen = pwd.Length;
                if (pwdLen < Constants.MinPasswordLength || pwdLen > Constants.MaxPasswordLength)
                {
                    TABControl.SelectedIndex = 0;
                    Logger.Error("lunghezza password non valida" + pwd);
                    System.Windows.MessageBox.Show("lunghezza password non valida" + pwd);
                    return;
                }

                // Salvo le credenziali su file per ricordarmele in futuro
                if (ChkRicorda.IsChecked == true)
                {
                    // Salvo le credenziali su file
                    //using(FileStream fs = new FileStream("credenziali.dat", FileMode.OpenOrCreate))
                    using (StreamWriter sw = new StreamWriter("credenziali.dat", false))
                    {
                        sw.WriteLine(username);
                        sw.WriteLine(pwd);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Errore Login", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);

                // Ritorno alla schermata di login
                TABControl.SelectedIndex = 0;
                return;
            }
            #endregion


            if (client.ClientLogin(username, pwd, ref this.authToken) == false)
            {
                TABControl.SelectedIndex = 0;
                return;
            }

            // Minimizzo la finestra
            this.WindowState = WindowState.Minimized;

            // Login e synch OK
            Logger.Info("Raccolgo l'xml");
            MyNotifyIcon.ShowBalloonTip(2000, "App status", "Indicizzando i file", ToolTipIcon.Info);
            await xmlGenerationTask;
            Logger.Info("Raccolto xml");
            XMLInstance.SaveToFile(Constants.XmlSavePath + @"\x.xml");

            // Prima synch
            try
            {
                statusInSynch = true;
                client.ClientSync(XMLInstance, this.authToken, deletedFilesList);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Errore Login", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);

                // Ritorno alla schermata di login
                TABControl.SelectedIndex = 0;
            }
            finally
            {
                statusInSynch = false;

                lock (_fswLocker)
                {
                    // Risveglio il fsw
                    Monitor.Pulse(_fswLocker);
                }
            }
            
            // Mi sposto nel main
            TABControl.SelectedIndex = 2;

            printXmlToTreeView();
            
            // Ripristino la finestra
            this.WindowState = WindowState.Normal;

            StartTimer();
        }

        /// <summary>
        /// funzione chiamata al premere del pulsante Registrati, controlla i dati inseriti e permette l'accesso a MainWindow
        /// </summary>
        private void BTNRegistration_Click(object sender, RoutedEventArgs e)
        {
            string username = "";
            string password = "";
            string passwordRep = "";

            #region Validazione input
            try
            {
                username = TXTusernameReg.Text;
                password = TXTPwdReg.Text;
                passwordRep = TXTPwdRepReg.Text;


                int usnLen = username.Length;
                if (usnLen < Constants.MinUsernameLength || usnLen > Constants.MaxUsernameLength)
                {
                    TABControl.SelectedIndex = 1;
                    Logger.Error("lunghezza username non valida" + usnLen);
                    System.Windows.MessageBox.Show("lunghezza username non valida" + usnLen);
                    return;
                }

                // Test password uguale passwordRep
                if (password.CompareTo(passwordRep) != 0)
                {
                    TABControl.SelectedIndex = 1;
                    Logger.Error("Le due password non corrispondono");
                    System.Windows.MessageBox.Show("Le due password non corrispondono");
                    //cancello i due campi
                    TXTPwdReg.Text= "";
                    TXTPwdRepReg.Text= "";
                    return;
                }

                int pwdLen = password.Length;
                if (pwdLen < Constants.MinPasswordLength || pwdLen > Constants.MaxPasswordLength)
                {
                    TABControl.SelectedIndex = 1;
                    Logger.Error("lunghezza password non valida" + pwdLen);
                    System.Windows.MessageBox.Show("lunghezza password non valida" + pwdLen);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Errore Registrazione", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);

                // Ritorno alla schermata di registrazione
                TABControl.SelectedIndex = 1;
                return;
            }
            #endregion
            
            if(client.ClientRegistration(username, password, ref this.authToken) == true)
            {
                //TODO benvenuto-tutorial
                TABControl.SelectedIndex = 2;
            }
            else
            {
                TABControl.SelectedIndex = 1;
                return;
            }            

            
        }
        #endregion

        #region SETTINGS TAB
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 4;
        }

        private void BTNSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            #region Validazione Input
            // PERCORSO CARTELLA SINCRONIZZAZIONE
            string synchPath = TXTpathCartella.Text;
            if (Utilis.IsValidPath(synchPath) == false) // Controllo solo se è sintatticamente correto. se non esiste lo creo dopo
            {
                Logger.Info("Inserito dbPath errato: " + synchPath);
                System.Windows.MessageBox.Show("Attenzione, il percorso da sincronizzare è errato", "Errore percorso synch", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //INTERVALLO TIMER
            int timerInterval = 0;
            try
            {
                timerInterval = Convert.ToInt32(NUDTimerValue.Value);
            }
            catch (Exception)
            {
                Logger.Error("Inserito intervallo timer errato: " + NUDTimerValue.Value);
                System.Windows.MessageBox.Show("Attenzione, valore intervallo timer errato", "Errore intervallo timer", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //IP SERVER
            string serverIP = TXTServerIP.Text;
            IPAddress address;
            if(IPAddress.TryParse(serverIP, out address))
            {
                if(address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    //IP non IPV4
                    Logger.Error("Inserito IP errato: " + serverIP);
                    System.Windows.MessageBox.Show("Attenzione, IP server errato", "Errore IP server", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                //IP non valido
                Logger.Error("Inserito IP errato: " + serverIP);
                System.Windows.MessageBox.Show("Attenzione, IP server errato", "Errore IP server", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //PORTA SERVER
            int serverPort = 0;
            try
            {
                serverPort = Convert.ToInt32(TXTServerPort.Text);
            }
            catch (Exception)
            {
                Logger.Error("Inserita porta server non valida: " + TXTServerPort.Text);
                System.Windows.MessageBox.Show("Attenzione, porta server non valida", "Errore porta server", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Se sono arrivato qua tutti i settings sono corretti
            Settings.SynchPath = synchPath;
            Settings.TimerFrequency = timerInterval * 1000; //converto secondi in millisecondi
            Settings.ServerIP = serverIP;
            Settings.ServerPort = serverPort;

            Settings.SaveSettings();
            #endregion

            Logger.Info("Nuove impostazioni salvate");
            
            // TODO rendere effettive le modifiche
        }

        private void BTNBrowseFolder_Clicked(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog();
            WinForms.DialogResult result = dialog.ShowDialog();
            TXTpathCartella.Text = dialog.SelectedPath;
            Properties.Settings.Default.Path = dialog.SelectedPath;

        }
        #endregion

        #region RESTORE TAB
        /// <summary>
        /// La funzione permette di accedere alla scheda contenente lo storico della cartella. 
        /// E' possibile procedere con il ripristino di una delle versioni precedenti
        /// </summary>
        private void BtnStoria_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 3;

            ///TODO faccio una Client.synch
            ///TODO invio la richiesta al server -> XML con i dati della cartella (history)
            ///TODO il server risponde inviandomi l'XML
            if (!File.Exists(@"D:\PDSCartellaPDS\fileXML.xml"))
            {
                throw new Exception();
            }
            XmlManager temp = new XmlManager(@"D:\PDSCartellaPDS\fileXML.xml");
            XElement root = temp.GetRoot();
            TRWRestore.Items.Clear();
            remoteVersionMap.Clear();

            TreeViewItem trwRoot = xmlToTreeViewRestore(root, "");
            trwRoot.Header = "Cartella Backup";

            TRWRestore.Items.Add(trwRoot);

            /*
            tv.Nodes.Add("node1", "Node 1");
            tv.Nodes.Add("node2", "Node 2");

            tv.Nodes["node1"].ForeColor = System.Drawing.Color.Blue;
            tv.Nodes["node2"].ForeColor = System.Drawing.Color.Black;*/

            ((TreeViewItem)TRWRestore.Items.GetItemAt(0)).IsExpanded = true;

        }


        private TreeViewItem xmlToTreeViewRestore(XElement el, string path)
        {
            TreeViewItem ret = new TreeViewItem();

            path += el.Attribute(XmlManager.DirectoryAttributeName).Value + Constants.PathSeparator;

            // Setto il nome della foglia nell'albero
            ret.Header = el.Attribute(XmlManager.DirectoryAttributeName).Value;

            // Visualizzo le cartelle ed effettuo la ricorsione
            foreach (XElement item in el.Elements(XmlManager.DirectoryElementName))
            {
                ret.Items.Add(xmlToTreeViewRestore(item, path));
            }

            // Visualizzo i file
            foreach (XElement fileElement in el.Elements(XmlManager.FileElementName))
            {
                // Creo la lista da inserire nella mappa
                List<VersionInfo> versionList = new List<VersionInfo>();

                // Percorso del file
                string fileName = fileElement.Attribute(XmlManager.FileAttributeName).Value;
                string filePath = path + fileName;

                // Ciclo sulle varie versioni
                foreach (XElement version in fileElement.Elements(XmlManager.VersionElementName))
                {
                    VersionInfo v = new VersionInfo();

                    // Setto i parametri per la versione
                    v.LastModTime = DateTime.Parse(version.Attribute(XmlManager.FileAttributeLastModTime).Value);
                    v.FileSize = Convert.ToInt64(version.Attribute(XmlManager.FileAttributeSize).Value);
                    v.Md5 = version.Attribute(XmlManager.FileAttributeChecksum).Value;
                    v.versionID = Convert.ToInt32(version.Attribute(XmlManager.VersionAttributeID).Value);

                    // Aggiungo alla lista 
                    versionList.Add(v);
                }

                // Aggiungo alla mappa
                remoteVersionMap.Add(filePath, versionList);

                TreeViewItem newItem = new TreeViewItem()
                {
                    Header = fileName,
                    Tag = filePath,

                };


                ret.Items.Add(newItem);
            }

            ret.Tag = path;
            return ret;
        }


        private void TRWGeneral_SelectedItemChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancello la lista delle versioni
                LSTFileVersion.Items.Clear();

                // Prendo l'oggetto selezionato
                TreeViewItem item = ((TreeViewItem)TRWRestore.SelectedItem);

                // Nel tag dell'oggetto avrò il suo percorso
                string fileRelPath = item.Tag.ToString();
                Logger.Debug("TRWGeneral selezionato: " + fileRelPath);

                // Controllo di avere qualche informazione
                if (remoteVersionMap.ContainsKey(fileRelPath) == true)
                {
                    // Prendo la lista dei contenitori delle informazioni
                    List<VersionInfo> versionList = remoteVersionMap[fileRelPath];
                    foreach (VersionInfo fileVersion in versionList)
                    {

                        ListBoxItem lstVersionItem = new ListBoxItem()
                        {
                            Content = "Versione " + fileVersion.versionID.ToString(),
                            Tag = fileVersion
                        };
                        LSTFileVersion.Items.Add(lstVersionItem);
                    }

                    // Carico le informazioni nelle label
                    VersionInfo info = XMLInstance.GetVersionInfo(fileRelPath);
                    if (info != null)
                    {
                        LBLLocalDateValue.Content = info.LastModTime.ToString(Constants.XmlDateFormat);
                        LBLLocalSizeValue.Content = Utilis.NormalizeSize(info.FileSize);
                    }
                    else
                    {
                        // se null può darsi che il file l'abbia cancellato
                        LBLLocalDateValue.Content = "";
                        LBLLocalSizeValue.Content = "";
                    }
                }
                else
                {
                    // Ho selezionato una cartella
                    LBLLocalDateValue.Content = "";
                    LBLLocalSizeValue.Content = "";
                    LBLRemoteDateValue.Content = "";
                    LBLRemoteSizeValue.Content = "";
                }

            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }

        }

        private void LSTFileVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ListBoxItem selectedVersionItem = ((ListBoxItem)LSTFileVersion.SelectedItem);
                if (selectedVersionItem == null)
                {
                    LSTFileVersion.UnselectAll();
                    return;
                }
                VersionInfo info = ((VersionInfo)selectedVersionItem.Tag);

                // Carico le informazioni nelle label
                LBLRemoteDateValue.Content = info.LastModTime.ToString(Constants.XmlDateFormat);
                LBLRemoteSizeValue.Content = Utilis.NormalizeSize(info.FileSize);
            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
        }

        private void BTNRestore_Click(object sender, RoutedEventArgs e)
        {

        }
        #endregion

        #region SYNCH
        private void BtnStartSynch_Click(object sender, RoutedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(Synch);
        }

        /// <summary>
        /// Wrapper per la sincronizzazione
        /// </summary>
        private void Synch(object taskState)
        {
            try
            {
                statusInSynch = true;

                StopTimer();
                client.ClientSync(XMLInstance, authToken, deletedFilesList);
                StartTimer();                               
                
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Errore synch", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
            finally
            {
                statusInSynch = false;

                lock (_fswLocker)
                {
                    // Risveglio il fsw
                    Monitor.Pulse(_fswLocker);
                }
            }
        }
        
        #endregion

    }

    public class VersionInfo
    {
        public string Md5;
        public DateTime LastModTime;
        public long FileSize;
        public int versionID;

        public string relPath;
        public bool deleted;
    }
    
    public enum TabIndexEnum : int
    {
        Login,
        Registration,
        Main,
        Restore,
        Settings
    }

    public class FSWEventListElement
    {
        public WatcherChangeTypes ChangeType;
        public string absPath;
    }

}
