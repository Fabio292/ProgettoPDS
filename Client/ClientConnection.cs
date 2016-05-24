using System;
using System.Net.Sockets;
using System.Threading;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Client
{
    class ClientConnection
    {

        private TcpClient conn = null;
        private bool isConnected = false;

        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
        }

        public ClientConnection()
        {
        }

        /// <summary>
        /// Disconnetto il client (se era precedentemente connesso)
        /// </summary>
        public void Disconnect()
        {
            if (this.isConnected == true)
            {
                this.isConnected = false;
                conn.Close();
            }
        }

        /// <summary>
        /// Connetto il client al server</summary>
        /// <exception cref="Exception">Eccezioni generiche</exception>
        /// <param name="ct">Cancellation token</param>
        public void Connect(CancellationToken ct)
        {
            // Controllo di non essere gia connesso
            if (this.isConnected == false)
            {
                conn = new TcpClient();
                this.isConnected = false;

                //TODO setto i parametri della connessione
                //conn.SendTimeout

                //TODO Prendere i dati dalla configurazione
                conn.Connect("127.0.0.1", 10000);

                this.isConnected = true;
                Logger.log("Aperta connessione verso il server");
            }
        }

        public TcpClient getTcpClient()
        {
            return this.conn;
        }


        /// <summary>
        /// analizza le differenze presenti tra la cartella lato client e quella lato server
        /// invia i files nuovi/modificati
        /// </summary>
        public void ClientSync(XmlManager xmlClient)
        {
            XElement xmlRootServer;
            XElement xmlRootClient;
            List<String> refList = new List<String>();

            // TODO Blocco il timer

            string r = "";
            string path = "";

            try
            {

            
                // Calcolo l'md5 del mio ultimo xml
                string md5XmlCLient = xmlClient.XMLDigest();
                string md5XmlServer = getXmlDigest(); // Scarico il digest dal server

                if (String.Compare(md5XmlCLient, md5XmlServer) == 0)
                {
                    //TODO resetto il timer
                    Logger.Info("Le due cartelle sono perfettamente uguali, non è necessario nessun aggiornamento");
                }
                else
                {
                    //le due cartelle sono diverse
                    Logger.Info("Le due cartelle sono diverse, creo elenco files diversi:\nMD5 Client: " + md5XmlCLient + "\nMD5 Server: " + md5XmlServer);
                    xmlRootClient = xmlClient.GetRoot();
                    xmlRootServer = XmlManager.GetRoot(getLastXml());

                    //guardo le differenze tra i due XDocuments e popolo r delle stringhe di richiesta da inviare al server
                    int elementsNumber = XmlManager.checkDiff(xmlRootClient, xmlRootServer, refList, ref r, path);
                    Logger.Info("l'elenco comandi inviati al server e': \n" + r);
                    Logger.Info("il numero di elementi da inviare al server e': " + elementsNumber);

                    int n = 0;
                    foreach (string filepath in refList)
                    {
                        n++;
                        Logger.Info("elemento numero " + n + ": " + filepath);
                    }

                    updateDirectory(xmlClient, elementsNumber, refList);

                }
            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")] Errore sincronizzazione con il server: " + e.Message);

            }

        }

        /// <summary>
        /// invia al server i files creati/modificati
        /// </summary>
        /// <param name="elementi">numero di elementsNumber da inviare al server</param>
        private void updateDirectory(XmlManager xmlClient, int elementi, List<String> refString)
        {
            // Devo inviare degli aggiornamenti, apro la synch
            Command cmd = new Command(CmdType.startSynch);
            Utilis.SendCmdSync(conn, cmd);

            // Ricevo la risposta del server
            Command resp = Utilis.GetCmdSync(conn);
            if (resp == null || resp.kmd != CmdType.ok)
            {
                Logger.Error("errore inizio synch");
                return;
            }

            //invio il numero di files che il server deve aspettarsi
            FileNumCommand numFiles = new FileNumCommand(elementi);
            Utilis.SendCmdSync(conn, numFiles);

            Logger.Info("Avvio la procedura di invio file al server. " + elementi + " file");

            //testare che la dimensione inviata sia corretta
            foreach (string filePath in refString)
            {
                //FileInfo f = new FileInfo(Constants.PathClient + filePath);
                //long dimensioneFile = f.Length;

                FileInfoCommand f = new FileInfoCommand(filePath);

                //invio la dimensione del file e il suo path separati da '#'
                //Command info = new Command(CmdType.numFile, filePath + '#' + dimensioneFile.ToString());
                Utilis.SendCmdSync(conn, f);

                Utilis.SendFile(conn, f.AbsFilePath, f.FileSize);
                Logger.Info("Ho inviato il file " + f.AbsFilePath);
            }

            // Chiudo la sessione di sincronizzazione
            resp = Utilis.GetCmdSync(conn);
            if (resp == null || resp.kmd != CmdType.ok)
            {
                Logger.Error("errore fine synch");
                return;
            }
            // Mando l'xml
            XmlCommand lastXml = new XmlCommand(xmlClient);
            Utilis.SendCmdSync(conn, lastXml);

            Command end = new Command(CmdType.endSynch);
            Utilis.SendCmdSync(conn, end);



            ////Gestisco e invio il nome delle cartelle vuote
            ////TODO controllare se è necessario piazzare i throw delle eccezioni (come quelle gestite nella classe FileInfoCommand)
            //FileNumCommand numEmptyDirs = new FileNumCommand(refListDir.Count);
            //Utilis.SendCmdSync(conn, numEmptyDirs);
            //foreach (string emptyDir in refListDir)
            //{
            //    Command c = new Command(CmdType.sendDir, emptyDir);

            //    Utilis.SendCmdSync(conn, c);
            //    Logger.Info("Ho inviato il nome della cartella vuota: " + c.Payload);
            //}
        }


        /// <summary>
        /// Ritorna l'md5 dell'XML dell'ultima versione posseduta dal server
        /// </summary>
        private string getXmlDigest()
        {
            //invio un messaggio di richiesta al server
            Command xmlDigestRequest = new Command(CmdType.getXmlDigest);
            Utilis.SendCmdSync(this.conn, xmlDigestRequest);

            //attendo la risposta da parte del server (mi invia l'md5)
            //TODO try
            XmlDigestCommand md5LastXmlServer = new XmlDigestCommand(Utilis.GetCmdSync(this.conn));

            if (md5LastXmlServer == null)
                throw new Exception("Aspettavo un comando contenente un md5, ricevuto nulla");
            if (md5LastXmlServer.kmd != CmdType.xmlDigest)
                throw new Exception("Aspettavo un comando di tipo xmlDigest, ricevuto " + md5LastXmlServer.kmd);

            return md5LastXmlServer.Digest;
        }

        /// <summary>
        ///ritorna l'XML dell'ultima versione posseduta dal server
        /// </summary>
        public XDocument getLastXml()
        {
            Command richiestaXml = new Command(CmdType.getXML);
            Utilis.SendCmdSync(this.conn, richiestaXml);

            //il client riceve l'xml completo
            XmlCommand lastXmlServer = new XmlCommand(Utilis.GetCmdSync(this.conn));

            if (lastXmlServer == null)
                throw new Exception("Aspettavo un comando contenente un Xml, ricevuto nulla");
            if (lastXmlServer.kmd != CmdType.Xml)
                throw new Exception("Aspettavo un comando di tipo Xml, ricevuto " + lastXmlServer.kmd);

            return XDocument.Parse(lastXmlServer.Payload);
        }

    }
}
