using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;


namespace Server
{
    public partial class MainWindow : Window
    {
        ServerListener server = null;
        CancellationTokenSource cts = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Server_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|          AVVIO SERVER              |");
            Logger.log("--------------------------------------");
            Logger.log("");
            
            server = ServerListener.Instance;
            lblStatusLeft.Text = "Server offline";

            TxtDbPath.Text = Settings.DbPath;
            TxtPort.Text = Settings.Port.ToString();

        }

        private void StartListening(object sender, RoutedEventArgs e)
        {
            #region validazione input
            string dbPath = TxtDbPath.Text;
            if(Utilis.IsValidPath(dbPath) == false) // Controllo solo se è sintatticamente correto. se non esiste lo creo dopo
            {
                Logger.Info("Inserito dbPath errato: " + dbPath);
                MessageBox.Show("Attenzione, il percorso del file del DB è errato", "Errore percorso DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Settings.DbPath = dbPath;

            try
            {
                int port = Convert.ToInt32(TxtPort.Text);
                Settings.Port = port;
            }
            catch (Exception)
            {
                Logger.Error("Inserita porta errata: " + TxtPort.Text);
                MessageBox.Show("Attenzione, il numero di porta è errato", "Errore porta", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            #endregion

            try
            {
                // Inizializzo i parametri
                server.setPort(Settings.Port);
                server.setDB(Settings.DbPath);

                // Avvio il listener
                server.ServerStart(cts.Token);

                #region Modifiche alla window
                BTNStartServer.Content = "Server in ascolto";
                lblStatusLeft.Text = "Server in ascolto su porta " + Settings.Port;

                BTNStartServer.IsEnabled = false;
                TxtPort.IsEnabled = false;
                TxtDbPath.IsEnabled = false;
                BtnChooseDbPath.IsEnabled = false;
                #endregion

                // Se arrivo a questo punto le impostazioni sono valide e le posso salvare
                Settings.SaveSettings();
            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
                MessageBox.Show("Impossibile aprire il server\n"+ex.Message, "Errore apertura server", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void ServerClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //cancello il token
            cts.Cancel();
            if(server != null)
                server.Shutdown();

            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|         CHIUSURA SERVER            |");
            Logger.log("--------------------------------------");
            Logger.log("");
        }

        private void BtnChooseDbPath_Click(object sender, RoutedEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            

            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".sqlite";
            dlg.Filter = "Sqlite DB|*.sqlite;*.db;*.db3;*.sqlite3|All files (*.*)|*.*";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                TxtDbPath.Text = filename;
            }
        }
    }
}
