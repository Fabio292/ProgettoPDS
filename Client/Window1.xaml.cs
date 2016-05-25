using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Diagnostics;



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
        private XmlManager XMLManager;
        private System.Windows.Forms.NotifyIcon MyNotifyIcon;
        private Dictionary<string, FileAttributeHelper> fileEditedMap = new Dictionary<string, FileAttributeHelper>();
        private System.Windows.Forms.ContextMenu menu_tray;

        static System.Timers.Timer TreeViewRefreshTimer;

        //private string savedUsername = "";
        //private string savedPwd = "";
        private string authToken = Constants.DefaultAuthToken;

        #region COSTRUTTORE - USCITA
        public Window1()
        {
            InitializeComponent();
            
            MyNotifyIcon = new System.Windows.Forms.NotifyIcon();
            MyNotifyIcon.Icon = new System.Drawing.Icon(Constants.IcoPath);

            MyNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(MyNotifyIcon_MouseDoubleClick);
            //MyNotifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(MyNotifyIcon_MouseClick);
            //MyNotifyIcon.Click += new System.Windows.Forms.MouseEventHandler(NotifyIcon_NotificationAreaClick);

            //gestione menu tray
            menu_tray = new System.Windows.Forms.ContextMenu();
            menu_tray.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Apri", new System.EventHandler(Show_Click)));
            menu_tray.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Chiudi", new System.EventHandler(Exit_Click)));
            MyNotifyIcon.ContextMenu = menu_tray;


            MyNotifyIcon.Visible = true;

            foreach (TabItem item in TABControl.Items)
            {
                item.Visibility = Visibility.Collapsed;
            }
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

            // genero l'xml
            // TODO fare in un thread a parte?
            XMLManager = new XmlManager(new DirectoryInfo(Constants.PathClient));
            XMLManager.SaveToFile(Constants.XmlSavePath + @"\x.xml");
            printXmlToTreeView();

            // Avvio timer e watcher
            StartWatcher();
            StopTimer();//StartTimer();

            //TODO vado a leggere le credenziali dal file, se esiste
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
            MyNotifyIcon.Visible = false;
            MyNotifyIcon.Dispose();

            cts.Cancel();
            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|        CHIUSURA CLIENT             |");
            Logger.log("--------------------------------------");
            Logger.log("");
        }

#endregion

        #region SYSTEM TRAY
        protected void Exit_Click(Object sender, System.EventArgs e)
        {
            this.Close();
        }

        protected void Show_Click(Object sender, System.EventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Show();
        }

        void MyNotifyIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Show();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                //this.ShowInTaskbar = false;
                //MyNotifyIcon.BalloonTipTitle = "App status";
                //MyNotifyIcon.BalloonTipText = "The app is minimized in the System Tray";
                MyNotifyIcon.ShowBalloonTip(20, "App status", "The app is minimized in the System Tray", ToolTipIcon.Info);
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this.ShowInTaskbar = true;
                //MyNotifyIcon.BalloonTipTitle = "App status";
                //MyNotifyIcon.BalloonTipText = "The app is currently running";
                MyNotifyIcon.ShowBalloonTip(20, "App status", "The app is currently running" , ToolTipIcon.Info);
            }

            base.OnStateChanged(e);
        }

        #endregion
        

        private void BtnStartSynch_Click(object sender, RoutedEventArgs e)
        {
            client.ClientSync(XMLManager, authToken);
        }


        #region TIMER
        private void ConfigureTimer()
        {
            TreeViewRefreshTimer = new System.Timers.Timer()
            {
                Interval = Constants.TreeViewRefreshTimerInterval,
                AutoReset = true,
                Enabled = false
            };

            TreeViewRefreshTimer.Elapsed += new ElapsedEventHandler(TreeViewRefreshTimerTick);
        }

        private void StartTimer()
        {
            TreeViewRefreshTimer.Enabled = true;
        }

        private void StopTimer()
        {
            TreeViewRefreshTimer.Enabled = false;
        }

        private void TreeViewRefreshTimerTick(object sender, ElapsedEventArgs e)
        {
            //TODO rimuovere!!!
            //XMLManager = new XmlManager(new DirectoryInfo(Constants.PathClient));
            Logger.log("test timer");
            //-----------------            

            try
            {
                //necessario per far si che il thread lanciato per la gestione dell'evento possa
                //interagire con il thread principale che gestisce l'interfaccia grafica
                Dispatcher.Invoke(new Action(() => {
                    printXmlToTreeView();
                }), System.Windows.Threading.DispatcherPriority.Background, cts.Token);
            }
            catch (TaskCanceledException ex)
            {
                // il task che ho lanciato per ridisegnare la treeview è stato cancellato (probabilmente chiusura dell'app)
                Logger.log(ex.Message);
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
            XElement root = XMLManager.GetRoot();
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
                Path = Constants.PathClient,

                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                //Watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime | NotifyFilters.Attributes;
                Filter = "*.*",
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024 //max possible buffer size
            };


            Watcher.Changed += new FileSystemEventHandler(OnChanged);
            Watcher.Created += new FileSystemEventHandler(OnChanged);
            Watcher.Deleted += new FileSystemEventHandler(OnChanged);
            Watcher.Renamed += new RenamedEventHandler(OnRenamed);

            Watcher.EnableRaisingEvents = false;
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
                if (e.ChangeType == WatcherChangeTypes.Changed)
                {
                    // IGNORO le notifiche di tipo change su directory
                    if (Utilis.IsDirectory(e.FullPath))
                        return;

                    FileAttributeHelper fileAttr = new FileAttributeHelper(e.FullPath);

                    XMLManager.RefreshFile(fileAttr);

                }
                else if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    if (Utilis.IsDirectory(e.FullPath))
                    {
                        // Creazione nuova cartella
                    }
                    else
                    {
                        FileAttributeHelper fileAttr = new FileAttributeHelper(e.FullPath);
                        XMLManager.CreateFile(fileAttr);
                    }
                    
                }
                else if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    // Non posso sapere se è file o directory
                    XMLManager.DeleteElement(e.FullPath);
                }


                XMLManager.SaveToFile(Constants.XmlSavePath + @"\x2.xml");
                Logger.Info(e.ChangeType +" " +  e.FullPath);

            }
            //catch (FileNotFoundException ex)
            //{
            //    Logger.error("OnChanged error: " + ex.Message);
            //}
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
                if (Utilis.IsDirectory(e.FullPath))
                {   // Directory
                    XMLManager.RenameDirectory(e.OldName, e.Name);
                }
                else
                {   // File
                    XMLManager.RenameFile(e.OldName, e.Name);
                }

                Logger.Info(e.ChangeType + " " + e.FullPath);

            }
            catch (FileNotFoundException ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
        }
        #endregion


        #region TAB CHANGE
        /// <summary>
        /// La funzione permette di accedere alla scheda contenente lo storico della cartella. 
        /// E' possibile procedere con il ripristino di una delle versioni precedenti
        /// </summary>
        private void BtnStoria_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 3;
        }


        /// <summary>
        /// funzione chiamata al premere del link "Non possiedi un account? Registrati", crea il nuovo account e redirezione su MainWindow
        /// </summary>
        private void AddAccount(object sender, RoutedEventArgs e)
        {
            //monto la scheda Registrazione
            TABControl.SelectedIndex = 1;
        }

        /// <summary>
        /// Penso serva a tornare nel main
        /// </summary>
        private void BackToMainButton(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 2;
        }
        #endregion

        #region User Interaction
        /// <summary>
        /// funzione chiamata al premere del pulsante Login, controlla i dati inseriti e permette l'accesso a Window1
        /// (sia per la checkLogin sia per la sendRegistration)
        /// </summary>
        private void checkLogin(object sender, RoutedEventArgs e)
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
                if(ChkRicorda.IsChecked == true)
                {
                    // Salvo le credenziali su file
                    //using(FileStream fs = new FileStream("credenziali.dat", FileMode.OpenOrCreate))
                    using(StreamWriter sw = new StreamWriter("credenziali.dat", false))
                    {
                        sw.WriteLine(username);
                        sw.WriteLine(pwd);
                    }
                }
            }
            catch (Exception ex)
            {
                //TODO visualizzare all'utente?
                System.Windows.MessageBox.Show(ex.Message, "Errore Login", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);

                // Ritorno alla schermata di login
                TABControl.SelectedIndex = 0;
                return;
            }
            #endregion
            

            if(client.ClientLogin(username, pwd, ref this.authToken) == true)
            {
                // Login OK
                TABControl.SelectedIndex = 2;
            }
            else
            {
                TABControl.SelectedIndex = 0;
                return;
            }
                
                

            try
            {
                client.ClientSync(XMLManager, this.authToken);
            }
            catch (Exception ex)
            {
                //TODO visualizzare all'utente?
                System.Windows.MessageBox.Show(ex.Message, "Errore Login", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);

                // Ritorno alla schermata di login
                TABControl.SelectedIndex = 0;
            }
        }
        
        /// <summary>
        /// funzione chiamata al premere del pulsante Registrati, controlla i dati inseriti e permette l'accesso a MainWindow
        /// </summary>
        private void sendRegistration(object sender, RoutedEventArgs e)
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
    }


}
