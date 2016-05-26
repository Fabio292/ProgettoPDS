using System;
using System.IO;
using System.Xml.Linq;

namespace Server
{
    static class Settings
    {
        private static readonly string _defaultDbPath = @"D:\PDSCartellaPDS\db.sqlite";
        private static string _dbPath = "";
        private static readonly string _dbPathElement = "db_path";

        private static readonly int _defaultPort = 10000;
        private static int _port = 0;
        private static readonly string _portElement = "port";

        private static readonly string settingsFile = "settings.xml";

        private static bool loaded = false;

        /// <summary>
        /// Percorso del file del DB
        /// </summary>
        public static string DbPath
        {
            get
            {
                if (loaded == false)
                    loadSettings();
                return _dbPath;
            }

            set
            {
                _dbPath = value;
            }
        }
        
        /// <summary>
        /// Porta da cui ascoltare le connessioni
        /// </summary>
        public static int Port
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


                    XElement dbPathElement = new XElement(_dbPathElement);
                    dbPathElement.Value = _dbPath;
                    root.Add(dbPathElement);

                    XElement portElement = new XElement(_portElement);
                    portElement.Value = _port.ToString();
                    root.Add(portElement);

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
                    _dbPath = lRoot.Element(_dbPathElement).Value;
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
            _dbPath = _defaultDbPath;
            _port = _defaultPort;
            SaveSettings();
            loaded = true;
        }

    }
}
