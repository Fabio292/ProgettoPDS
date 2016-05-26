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
            
        }

        private void StartListening(object sender, RoutedEventArgs e)
        {
            //if (server.Running == true)
            //{
            //    cts.Cancel();
            //    server.Shutdown();
            //    BTNStartServer.Content = "Avvia server";
            //    lblStatusLeft.Text = "Server NON in ascolto";
            //}
            //else
            //{
            //    //Inizializzo i parametri
            //    server.setPort(10000);
            //    server.setDB(Constants.ServerDBPath);
            //    server.ServerStart(cts.Token);
            //    BTNStartServer.Content = "Interrompi server";
            //    lblStatusLeft.Text = "Server in ascolto";
            //}

            //Inizializzo i parametri
            server.setPort(10000);
            server.setDB(Constants.ServerDBPath);

            server.ServerStart(cts.Token);

            //BTNStartServer.Content = "Server in ascolto...";
            lblStatusLeft.Text = "Server in ascolto";
            //BTNStartServer.IsEnabled = false;
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
        
    }
}
