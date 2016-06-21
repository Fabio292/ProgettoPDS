using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Xml.Linq;


namespace Server
{
    class XmlManager
    {
        #region NOMI DEI CAMPI
        public static readonly string DirectoryElementName = "dir";
        public static readonly string DirectoryAttributeName = "name";

        public static readonly string VersionElementName = "version";
        public static readonly string VersionAttributeID = "id";
        public static readonly string VersionAttributeLastModTime = "modTime";
        public static readonly string VersionAttributeSize = "dim";
        public static readonly string VersionAttributeChecksum = "md5";
        public static readonly string VersionAttributeLastVersion = "lastVersion";
        public static readonly string VersionAttributeDeleted = "deleted";

        public static readonly string FileElementName = "file";
        public static readonly string FileAttributeName = "name";
        public static readonly string FileAttributeLastModTime = "modTime";
        public static readonly string FileAttributeSize = "dim";
        public static readonly string FileAttributeChecksum = "md5";
        #endregion

        private XDocument _xmlDoc;

        /// <summary>
        /// carico l'xml da un file
        /// </summary>
        /// <param name="xmlPath">Percorso assoluto del file</param>
        public XmlManager(string xmlPath)
        {
            this._xmlDoc = XDocument.Load(xmlPath);
        }

        /// <summary>
        /// Genero l'xml andando a leggere il contenuto di una cartella
        /// </summary>
        /// <param name="_dir"></param>
        public XmlManager(DirectoryInfo _dir)
        {
            this._xmlDoc = new XDocument(CreateFileSystemXmlTree(_dir));
        }

        /// <summary>
        /// Clono un'istanza di XMLmanager
        /// </summary>
        /// <param name="doc"></param>
        public XmlManager(XDocument doc)
        {
            this._xmlDoc = doc;
        }

        /// <summary>
        /// Genero un xml andando a leggere i dati dal DB
        /// </summary>
        /// <param name="conn">Connessione al DB</param>
        /// <param name="UID">ID dell'utente</param>
        public XmlManager(SQLiteConnection conn, int UID, int versionID)
        {
            XElement root = new XElement(XmlManager.DirectoryElementName);
            root.SetAttributeValue(XmlManager.DirectoryAttributeName, "_");

            using (SQLiteCommand sqlCmd = conn.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT PathClient, MD5, LastModTime, Size FROM Versioni WHERE UID = @_UID AND LastVersion = 1 AND Deleted = 0;";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                //sqlCmd.Parameters.AddWithValue("@_versionID", versionID);

                using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                {
                    string relPath;
                    string md5;
                    DateTime lastModTime;
                    int fileSize;

                    while (reader.Read())
                    {
                        // Analizzo ogni elemento ricordando che dal DB tiro fuori SOLO FILE
                        relPath = reader.GetString(0);
                        md5 = reader.GetString(1);
                        lastModTime = reader.GetDateTime(2);
                        fileSize = reader.GetInt32(3);

                        string fname = Path.GetFileName(relPath);
                        string[] dirPath = Path.GetDirectoryName(relPath).Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);


                        // Cerco la dir in cui va il file e se non c'e la creo
                        XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath), root);
                        if (dir == null)
                        {
                            #region ricerca e creazione parziale
                            dir = root;
                            bool create = false;
                            foreach (var item in dirPath)
                            {
                                if (create == false)
                                {
                                    // sto ancora cercando
                                    var dirAus = this.getDirectoryElement(item, dir);
                                    if (dirAus != null)
                                    {
                                        dir = dirAus;
                                        continue;
                                    }
                                    else
                                        create = true;
                                }

                                XElement newDir = new XElement(XmlManager.DirectoryElementName);
                                newDir.SetAttributeValue(XmlManager.DirectoryAttributeName, item);

                                dir.Add(newDir);
                                dir = newDir;
                            }
                            #endregion
                        }

                        // A questo punto 'dir' sarà la cartella in cui ci andrà il file
                        XElement fileElement = new XElement(XmlManager.FileElementName);

                        fileElement.SetAttributeValue(FileAttributeName, fname);
                        fileElement.SetAttributeValue(FileAttributeLastModTime, lastModTime.ToString(Constants.XmlDateFormat));
                        fileElement.SetAttributeValue(FileAttributeSize, fileSize.ToString());
                        fileElement.SetAttributeValue(FileAttributeChecksum, md5);

                        dir.Add(fileElement);

                    }

                }
            }

            this._xmlDoc = new XDocument(root);
        }

        #region OLD
        //public XmlManager(SQLiteConnection conn, int UID, int versionID)
        //{
        //    XElement root = new XElement(XmlManager.DirectoryElementName);
        //    root.SetAttributeValue(XmlManager.DirectoryAttributeName, "_");


        //    using (SQLiteCommand sqlCmd = conn.CreateCommand())
        //    {
        //        sqlCmd.CommandText = @"SELECT PathClient, MD5, LastModTime, Size FROM Versioni WHERE UID = @_UID AND VersionID = @_versionID AND Deleted = 0;";
        //        sqlCmd.Parameters.AddWithValue("@_UID", UID);
        //        sqlCmd.Parameters.AddWithValue("@_versionID", versionID);

        //        using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
        //        {
        //            string relPath;
        //            string md5;
        //            DateTime lastModTime;
        //            int fileSize;

        //            while (reader.Read())
        //            {
        //                // Analizzo ogni elemento ricordando che dal DB tiro fuori SOLO FILE
        //                relPath = reader.GetString(0);
        //                md5 = reader.GetString(1);
        //                lastModTime = reader.GetDateTime(2);
        //                fileSize = reader.GetInt32(3);

        //                string fname = Path.GetFileName(relPath);
        //                string[] dirPath = Path.GetDirectoryName(relPath).Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);


        //                // Cerco la dir in cui va il file e se non c'e la creo
        //                XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath), root);
        //                if (dir == null)
        //                {
        //                    #region ricerca e creazione parziale
        //                    dir = root;
        //                    bool create = false;
        //                    foreach (var item in dirPath)
        //                    {
        //                        if (create == false)
        //                        {
        //                            // sto ancora cercando
        //                            var dirAus = this.getDirectoryElement(item, dir);
        //                            if (dirAus != null)
        //                            {
        //                                dir = dirAus;
        //                                continue;
        //                            }
        //                            else
        //                                create = true;
        //                        }

        //                        XElement newDir = new XElement(XmlManager.DirectoryElementName);
        //                        newDir.SetAttributeValue(XmlManager.DirectoryAttributeName, item);

        //                        dir.Add(newDir);
        //                        dir = newDir;
        //                    }
        //                    #endregion
        //                }

        //                // A questo punto 'dir' sarà la cartella in cui ci andrà il file
        //                XElement fileElement = new XElement(XmlManager.FileElementName);

        //                fileElement.SetAttributeValue(FileAttributeName, fname);
        //                fileElement.SetAttributeValue(FileAttributeLastModTime, lastModTime.ToString(Constants.XmlDateFormat));
        //                fileElement.SetAttributeValue(FileAttributeSize, fileSize.ToString());
        //                fileElement.SetAttributeValue(FileAttributeChecksum, md5);

        //                dir.Add(fileElement);

        //            }

        //        }
        //    }

        //    this._xmlDoc = new XDocument(root);
        //}
        #endregion

        /// <summary>
        /// Genero un xml andando a leggere i dati dal DB con versioni
        /// </summary>
        /// <param name="conn">Connessione al DB</param>
        /// <param name="UID">ID dell'utente</param>
        public XmlManager(SQLiteConnection conn, int UID)
        {
            XElement root = new XElement(XmlManager.DirectoryElementName);
            root.SetAttributeValue(XmlManager.DirectoryAttributeName, "");


            using (SQLiteCommand sqlCmd = conn.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT PathClient, MD5, LastModTime, Size, VersionID, LastVersion, Deleted FROM Versioni WHERE UID = @_UID ORDER BY VersionID DESC;";
                //sqlCmd.Parameters.AddWithValue("@_latestV", latestVersionId);
                sqlCmd.Parameters.AddWithValue("@_UID", UID);

                using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                {
                    string relPath;
                    string md5;
                    DateTime lastModTime;
                    int fileSize;
                    int versionID;
                    bool lastVersion;
                    bool deleted;

                    while (reader.Read())
                    {
                        // Analizzo ogni elemento ricordando che dal DB tiro fuori SOLO VERSIONI
                        relPath = reader.GetString(0);
                        md5 = reader.GetString(1);
                        lastModTime = reader.GetDateTime(2);
                        fileSize = reader.GetInt32(3);
                        versionID = reader.GetInt32(4);
                        lastVersion = reader.GetBoolean(5);
                        deleted = reader.GetBoolean(6);

                        string fname = Path.GetFileName(relPath);
                        string[] dirPath = Path.GetDirectoryName(relPath).Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);


                        // Cerco la dir in cui va il file e se non c'e la creo
                        XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath), root);
                        if (dir == null)
                        {
                            #region ricerca e creazione parziale
                            dir = root;
                            bool create = false;
                            foreach (var item in dirPath)
                            {
                                if (create == false)
                                {
                                    // sto ancora cercando
                                    var dirAus = this.getDirectoryElement(item, dir);
                                    if (dirAus != null)
                                    {
                                        dir = dirAus;
                                        continue;
                                    }
                                    else
                                        create = true;
                                }

                                XElement newDir = new XElement(XmlManager.DirectoryElementName);
                                newDir.SetAttributeValue(XmlManager.DirectoryAttributeName, item);

                                dir.Add(newDir);
                                dir = newDir;
                            }
                            #endregion
                        }

                        // A questo punto 'dir' sarà la cartella in cui ci andrà il file
                        // Cerco il file
                        XElement file = this.getFileElement(relPath, root);

                        if(file == null) //Il file non è presente, lo aggiungo e poi passo ad aggiungere le versioni
                        {
                            file = new XElement(XmlManager.FileElementName);
                            file.SetAttributeValue(FileAttributeName, fname);
                            dir.Add(file);
                        }

                        XElement version = new XElement(XmlManager.VersionElementName);

                        version.SetAttributeValue(VersionAttributeLastModTime, lastModTime.ToString(Constants.XmlDateFormat));
                        version.SetAttributeValue(VersionAttributeSize, fileSize.ToString());
                        version.SetAttributeValue(VersionAttributeChecksum, md5);
                        version.SetAttributeValue(VersionAttributeID, versionID);
                        version.SetAttributeValue(VersionAttributeLastVersion, lastVersion);
                        version.SetAttributeValue(VersionAttributeDeleted, deleted);

                        // Aggiungo la versione al file
                        file.Add(version);
                    }

                }
            }

            this._xmlDoc = new XDocument(root);
        }

        /// <summary>
        /// Genero un file per il'xml
        /// </summary>
        /// <param name="path">Percorso assoluto dove salvare l'xml</param>
        static public void InitializeXmlFile(string path)
        {
            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine(@"<dir name=""ClientSide"">");
                sw.WriteLine(@"</dir>");
            }
        }


        private XElement CreateFileSystemXmlTree(DirectoryInfo di)
        {
            return new XElement(DirectoryElementName, new XAttribute(DirectoryAttributeName, di.Name),
                from file in di.GetFiles()
                select new XElement(FileElementName, new XAttribute(FileAttributeName, file.Name), new XAttribute(FileAttributeLastModTime, file.LastWriteTime.ToString(Constants.XmlDateFormat)), new XAttribute(FileAttributeSize, file.Length), new XAttribute(FileAttributeChecksum, Utilis.MD5sum(file))),
                from d in di.GetDirectories()
                select CreateFileSystemXmlTree(d)
            );
        }


        /// <summary>
        /// Cancello una directory dall'albero (e gli eventuali sottoelementi). Ritorno true se era file, false se era directory
        /// </summary>
        /// <param name="relPath">Percorso relativo della directory cancellata</param>
        public void DeleteElement(string relPath, List<string> deletedFiles)
        {
            //string path = Utilis.AbsToRelativePath(absPath, Settings.SynchPath);
            XElement el = this.getDirectoryElement(relPath);

            // Controllo se ho effettivamente trovato l'elemento, se è null allora significa che è un file
            if (el == null)
            {
                el = this.getFileElement(relPath);
                deletedFiles.Add(relPath);
            }
            else
            {
                // Richiamo sulla cartella
                searchFileRecursive(el, relPath, deletedFiles);
            }

            el.Remove();
        }


        /// <summary>
        /// Cerco nell'elemento passato tutti i file e li aggiungo alla lista
        /// </summary>
        /// <param name="e">Elemento directory</param>
        /// <param name="path">Percorso parziale</param>
        /// <param name="deletedFiles">Lista file cancellati</param>
        private void searchFileRecursive(XElement e, string path, List<string> deletedFiles)
        {
            // Sottocartelle sulle quali ricorrere
            var subDirs = e.Elements(DirectoryElementName);

            foreach (var subdir in subDirs)
            {
                // Nuovo percorso
                string np = path + Constants.PathSeparator + subdir.Attribute(XmlManager.DirectoryAttributeName).Value;

                // Ricorro sulle sottocartelle
                searchFileRecursive(subdir, np, deletedFiles);
            }

            // Trovo i file presenti in questa cartella
            var subFiles = e.Elements(FileElementName);

            foreach (var file in subFiles)
            {
                // Li aggiungo alla lista
                deletedFiles.Add(path + Constants.PathSeparator + file.Attribute(XmlManager.FileAttributeName).Value);
            }


        }

        #region Ricerca Elementi
        /// <summary>
        /// Cerco all'interno del documento xml un elemento di tipo directory </summary>
        /// <param name="relPath">Il percorso RELATIVO alla directory per cui è stato generato il documento della directory da cercare</param>
        /// <returns></returns>
        private XElement getDirectoryElement(string relPath, XElement src = null)
        {
            // Estraggo gli elementi del percorso per scendere nell'albero dell'xml
            string[] pathElement = relPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            XElement ret;

            if(src == null)
                ret = this.GetRoot();
            else
                ret = src;

            // prendo il FIRST dell'array ritornato dalla where perchè il filesystem mi garantisce l'univocità dei nomi
            // degli oggetti (directory o file) presenti in una directory
            foreach (string elem in pathElement)
            {
                ret = ret.Elements(DirectoryElementName).Where(elt => elt.Attribute(DirectoryAttributeName).Value == elem).FirstOrDefault();
                if (ret == null)
                    break;
            }

            return ret;
        }


        /// <summary>
        /// Cerca nel documento corrente l'elemento corrispondente al file il cui percorso è passato come parametro </summary>
        /// <param name="relPath">Percorso RELATIVO alla cartella per cui è stato generato il documento</param>
        /// <returns>L'elemento XML corrispondente</returns>
        private XElement getFileElement(string relPath, XElement src = null)
        {
            XElement ret = null;
            XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath), src);

            ret = dir.Elements(FileElementName).Where(elt => elt.Attribute(FileAttributeName).Value == Path.GetFileName(relPath)).FirstOrDefault();

            return ret;
        }
        #endregion


        #region CONFRONTO XML

        /// <summary>
        /// funzione che elenca ricorsivamente tutti i files e tutte le cartelle presenti 
        /// all'interno della cartella interessata (root)
        /// </summary>
        /// <param name="root">XElement cartella interessata</param>
        /// <param name="subElements">stringa che memorizza l'elenco dei path dei file diversi</param>
        /// <param name="path">path temporaneo</param>
        public static int getSubdirs(XElement root, List<String> refList, ref string subElements, string path)
        {
            int counter = 0;
            var files = from f in root.Elements(FileElementName)
                        select f;
            var dirs = from d in root.Elements(DirectoryElementName)
                       select d;

            foreach (XElement e in files)
            {
                subElements += "PUSH" + path + "\\" + e.Attribute("name").Value + "\n";
                refList.Add(path + "\\" + e.Attribute("name").Value);
                counter++;
            }
            foreach (XElement e in dirs)
            {
                //subElements += path + "\\" + e.Attribute("name").Value + "\n";
                counter += getSubdirs(e, refList, ref subElements, path + "\\" + e.Attribute("name").Value);
            }

            return counter;
        }
        #endregion


        /// <summary>
        /// Ritorna l'md5 dell'XML NORMALIZZATO</summary>
        public string XMLDigest()
        {
            // "Normalizzo" l'xml andando a cancellare l'attributo della root
            XDocument doc = new XDocument(this._xmlDoc);
            XElement root = XmlManager.GetRoot(doc);

            root.Attribute(XmlManager.DirectoryAttributeName).Value = "";

            // Cancellare le directory vuote
            removeEmpty(root);

            // Ordinare le cose
            XmlManager.sortElement(root);

            //try
            //{
            //    string outPath = Constants.XmlSavePath + Constants.PathSeparator + "S_DIGEST.xml";
            //    using (StreamWriter output = new StreamWriter(outPath))
            //    {
            //        output.Write(doc.ToString());
            //    }
            //}
            //catch (Exception)
            //{
            //    Logger.Error("DIAMINE");
            //}

            return Utilis.Md5String(doc.ToString());
        }

        #region NORMALIZZAZIONE
        /// <summary>
        /// Cancello le directory vuote dall'xml per la normalizzazione
        /// </summary>
        /// <param name="e">La root</param>
        private int removeEmptySubDir(XElement e)
        {
            int numsubFiles = 0;
            var subdirsC = e.Elements(DirectoryElementName).ToList();


            foreach (var subdir in subdirsC)
            {
                numsubFiles += removeEmptySubDir(subdir);
            }

            var subFilesC = e.Elements(FileElementName);

            numsubFiles += subFilesC.Count();


            if (numsubFiles == 0)
                e.Remove();

            return numsubFiles;

        }

        /// <summary>
        /// Funzione per normalizzare un xml eliminando le sottocartelle vuote
        /// </summary>
        /// <param name="root">La ROOT dell'xml</param>
        private void removeEmpty(XElement root)
        {
            var subdirsC = root.Elements(DirectoryElementName);

            foreach (var subdir in subdirsC)
            {
                removeEmptySubDir(subdir);
            }
        }

        /// <summary>
        /// Ordino un elemento mettendo prima le cartelle (ordinate per nome) e poi i file
        /// </summary>
        /// <param name="el">elemento di partenza da ordinare</param>
        private static void sortElement(XElement el)
        {
            // Creo le liste ordinate
            var subDir = el.Elements(XmlManager.DirectoryElementName).OrderBy(s => (string)s.Attribute(XmlManager.DirectoryAttributeName)).ToList();
            var subFile = el.Elements(XmlManager.FileElementName).OrderBy(s => (string)s.Attribute(XmlManager.FileAttributeName)).ToList();

            // Cancello il contenuto
            el.RemoveNodes();

            // Riaggiungo le directory ordinate
            foreach (var item in subDir)
            {
                el.Add(item);
            }

            // Riaggiungo i file ordinati
            foreach (var item in subFile)
            {
                el.Add(item);
            }

            // Ricorro sulle sottocartelle
            foreach (var item in subDir)
            {
                sortElement(item);
            }
        }
        #endregion



        /// <summary>
        /// Salvo su file una versione normalizzata dell'xml
        /// </summary>
        /// <param name="path">Percorso dove salvare l'xml</param>
        public void SaveToFile(string path)
        {
            using (StreamWriter output = new StreamWriter(path))
            {
                // "Normalizzo" l'xml andando a cancellare l'attributo della root
                XDocument doc = new XDocument(this._xmlDoc);
                XElement root = XmlManager.GetRoot(doc);

                root.Attribute(XmlManager.DirectoryAttributeName).Value = "";

                // Cancellare le directory vuote
                removeEmpty(root);

                // Ordinare le cose
                XmlManager.sortElement(root);


                output.Write(doc.ToString());
            }
        }


        #region GETTER
        public static XElement GetRoot(XDocument xDoc)
        {
            return xDoc.Root;
        }

        public override string ToString()
        {
            return _xmlDoc.ToString();
        }

        public XDocument getXDocument()
        {
            return _xmlDoc;
        }

        public XElement GetRoot()
        {
            return _xmlDoc.Element(DirectoryElementName);
        }
        #endregion

    }
}
