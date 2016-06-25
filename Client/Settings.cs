using System;
using System.IO;
using System.Xml.Linq;

namespace Client
{
    static class Settings
    {
        public static readonly string _defaultSynchPath = @"C:\PDSCartellaPDS\ClientSide";
        private static string _synchPath = "";
        private static readonly string _synchPathElement = "SynchPath";

        public static readonly int _defaultTimerFrequency = 60000;
        private static int _timerFrequency = 0;
        private static readonly string _timerFrequencyElement = "TimerFrequency";

        private static readonly int _defaultPort = 10000;
        private static int _port = 0;
        private static readonly string _portElement = "ServerPort";

        private static readonly string _defaultIP = "127.0.0.1";
        private static string _IP = "";
        private static readonly string _IPElement = "ServerIP";

        //TODO Colori

        private static readonly string settingsFile = "settings.xml";

        private static bool loaded = false;

        /// <summary>
        /// Percorso della cartella da sincronizzare
        /// </summary>
        public static string SynchPath
        {
            get
            {
                if (loaded == false)
                    loadSettings();
                return _synchPath;
            }

            set
            {
                _synchPath = value;
            }
        }
        
        /// <summary>
        /// Intervallo per richiamare il timer di synch
        /// </summary>
        public static int TimerFrequency
        {
            get
            {
                if (loaded == false)
                    loadSettings();
                return _timerFrequency;
            }

            set
            {
                _timerFrequency = value;
            }
        }

        /// <summary>
        /// Indirizzo IP del server
        /// </summary>
        public static string ServerIP
        {
            get
            {
                if (loaded == false)
                    loadSettings();
                return _IP;
            }

            set
            {
                _IP = value;
            }
        }

        /// <summary>
        /// Porta del server
        /// </summary>
        public static int ServerPort
        {
            get
            {
                if (loaded == false)
                    loadSettings();
                return _port;
            }

            set
            {
                _port = value;
            }
        }



        /// <summary>
        /// Salvo le variabili su file
        /// </summary>
        public static void SaveSettings()
        {
            try
            {
                using(FileStream ws = new FileStream(settingsFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    XDocument doc = new XDocument();
                    XElement root = new XElement("settings");


                    XElement synchPathElement = new XElement(_synchPathElement);
                    synchPathElement.Value = _synchPath;
                    root.Add(synchPathElement);

                    XElement timerFreqElement = new XElement(_timerFrequencyElement);
                    timerFreqElement.Value = _timerFrequency.ToString();
                    root.Add(timerFreqElement);

                    XElement serverIPElement = new XElement(_IPElement);
                    serverIPElement.Value = _IP;
                    root.Add(serverIPElement);

                    XElement serverPortElement = new XElement(_portElement);
                    serverPortElement.Value = _port.ToString();
                    root.Add(serverPortElement);

                    doc.Add(root);
                    doc.Save(ws);
                }
            }
            catch (Exception e)
            {
                Logger.Error("Errore salvataggio impostazioni: " + e);
            }

        }


        private static void loadSettings()
        {
            try
            {

                using (FileStream lStream = new FileStream(settingsFile, FileMode.Open, FileAccess.Read))
                {
                    // Carico l'xml
                    XElement lRoot = XElement.Load(lStream);

                    // Leggo i valori
                    _synchPath = lRoot.Element(_synchPathElement).Value;
                    _timerFrequency = Convert.ToInt32(lRoot.Element(_timerFrequencyElement).Value);
                    _IP = lRoot.Element(_IPElement).Value;
                    _port = Convert.ToInt32(lRoot.Element(_portElement).Value);

                    loaded = true;
                }

            }
            catch (Exception)
            {
                loadDefaultSettings();
            }
        }

        private static void loadDefaultSettings()
        {
            _synchPath = _defaultSynchPath;
            _timerFrequency = _defaultTimerFrequency;
            _IP = _defaultIP;
            _port = _defaultPort;


            SaveSettings();
            loaded = true;
        }

    }
}
