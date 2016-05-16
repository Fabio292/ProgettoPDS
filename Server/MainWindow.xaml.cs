using System.Threading;
using System.Windows;


namespace Server
{
    public partial class MainWindow : Window
    {
        ServerListener server;
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
            
        }

        private void StartListening(object sender, RoutedEventArgs e)
        {
            server = new ServerListener();
            server.ServerStart(cts.Token);
        }

        private void ServerClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //cancello il token
            cts.Cancel();

            Logger.log("");
            Logger.log("--------------------------------------");
            Logger.log("|         CHIUSURA SERVER            |");
            Logger.log("--------------------------------------");
            Logger.log("");
        }
        
    }
}
