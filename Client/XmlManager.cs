using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Xml.Linq;


namespace Client
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
            //SaveToFile(@"C:\Users\Utente\Desktop\out.xml");
        }

        /// <summary>
        /// Clono un'istanza di XMLmanager
        /// </summary>
        /// <param name="doc"></param>
        public XmlManager(XDocument doc)
        {
            this._xmlDoc = doc;
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

        #region Modifiche da FSW

        /// <summary>
        /// Rinomina l'entry di una directory nell'xml 
        /// </summary>
        /// <param name="oldRelPath">Vecchio percorso</param>
        /// <param name="newRelPath">Nuovo Percorso</param>
        public void RenameDirectory(string oldRelPath, string newRelPath)
        {
            // Nuovo nome della cartella
            string newName = newRelPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Last();

            XElement dirElement = getDirectoryElement(oldRelPath, this.GetRoot());
            
            // Modifico il nome dell'attributo
            dirElement.Attribute(DirectoryAttributeName).Value = newName;
        }

        /// <summary>
        /// Rinomina l'entry di un file nell'xml 
        /// </summary>
        /// <param name="oldRelPath">Vecchio percorso</param>
        /// <param name="newRelPath">Nuovo Percorso</param>
        public void RenameFile(string oldRelPath, string newRelPath)
        {
            // Nome del nuovo file
            string newName = newRelPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Last();

            XElement fileElement = getFileElement(oldRelPath, this.GetRoot());
            
            // Modifico il nome dell'attributo
            fileElement.Attribute(FileAttributeName).Value = newName;
        }
        
        /// <summary>
        /// Aggiorno l'entry di un file ricalcolandone l'md5 e data di ultima modifica
        /// </summary>
        /// <param name="fa">Classe contenente gli attributi del file</param>
        public void RefreshFile(FileAttributeHelper fa)
        {
            //FileInfo fInfo = new FileInfo(Utilis.RelativeToAbsPath(relPath));
            XElement fileElement = getFileElement(fa.RelFilePath, this.GetRoot());
            
            // Controllo se veramente modificare gli attributi (causa notifiche spurie)
            if(fileElement.Attribute(FileAttributeSize).Value.CompareTo(fa.Size.ToString()) == 0)
            {
                // Dimensione uguale.. controllo la data di ultima modifica
                if (fileElement.Attribute(FileAttributeLastModTime).Value.CompareTo(fa.LastModtime.ToString(Constants.XmlDateFormat)) == 0)
                {
                    // Data di ultima modifica uguale, non tocco nulla
                    Logger.Debug("Non tocco nulla per il file " + Path.GetFileName(fa.AbsFilePath));
                    return;
                }
            }

            // Se sono arrivato qua c'era qualcosa di diverso
            fileElement.Attribute(FileAttributeChecksum).Value = fa.Md5;
            fileElement.Attribute(FileAttributeSize).Value = fa.Size.ToString();
            fileElement.Attribute(FileAttributeLastModTime).Value = fa.LastModtime.ToString(Constants.XmlDateFormat);         
        }
        
        /// <summary>
        /// Cancello una directory dall'albero (e gli eventuali sottoelementi). Ritorno true se era file, false se era directory
        /// </summary>
        /// <param name="absPath">Percorso assoluto della directory cancellata</param>
        public void DeleteElement(string absPath, List<string> deletedFiles)
        {
            string path = Utilis.AbsToRelativePath(absPath, Settings.SynchPath);
            XElement el = getDirectoryElement(path, this.GetRoot());

            // Controllo se ho effettivamente trovato l'elemento, se è null allora significa che è un file
            if (el == null)
            {
                el = getFileElement(path, this.GetRoot());
                deletedFiles.Add(@"\" + path);
            }
            else
            {
                // Richiamo sulla cartella
                if (path.StartsWith(@"\") == false)
                    path = Constants.PathSeparator + path;

                searchFileRecursive(el, path, deletedFiles);
            }

            el.Remove();
        }

        /// <summary>
        /// Creo un elemento di tipo file
        /// </summary>
        /// <param name="fa">Classe contenente gli attributi del file</param>
        public void CreateFile(FileAttributeHelper fa)
        {
            XElement dir = getDirectoryElement(Path.GetDirectoryName(fa.RelFilePath), this.GetRoot());

            // Creo l'oggetto da inserire
            XElement file = new XElement(FileElementName);

            // Aggiungo gli attributi
            file.SetAttributeValue(FileAttributeName, Path.GetFileName(fa.RelFilePath));
            file.SetAttributeValue(FileAttributeLastModTime, fa.LastModtime.ToString(Constants.XmlDateFormat));

            // Ottimizzazioni: alla creazione il file è vuoto
            file.SetAttributeValue(FileAttributeSize, 0);
            file.SetAttributeValue(FileAttributeChecksum, Constants.EmptyFileDigest); 

            dir.Add(file);
        }

        /// <summary>
        /// Vado a leggere una nuova directory
        /// </summary>
        /// <param name="absPath">Percorso assoluto della nuova cartella</param>
        public void CreateDirectory(string absPath)
        {
            //Nome della cartella
            //string dirName = Path.GetDirectoryName(absPath);
            string dirName = absPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Last();

            //Trovo la directory superiore corrispondente
            string parentDir = Path.GetDirectoryName(Utilis.AbsToRelativePath(absPath, Settings.SynchPath));
            //DirectoryInfo dirInfo = new DirectoryInfo(absPath);

            XElement parentDirElem = getDirectoryElement(parentDir, this.GetRoot());

            //creo l'elemento e lo inserisco nel padre
            XElement dirElem = new XElement(XmlManager.DirectoryElementName);
            dirElem.SetAttributeValue(XmlManager.DirectoryAttributeName, dirName);

            // Chiamo la funzione ricorsiva usata per generare l'xml
            parentDirElem.Add(dirElem);

        }

        #endregion

        #region Ricerca Elementi
        /// <summary>
        /// Cerco all'interno del documento xml un elemento di tipo directory </summary>
        /// <param name="relPath">Il percorso RELATIVO alla directory per cui è stato generato il documento della directory da cercare</param>
        /// <returns></returns>
        private static XElement getDirectoryElement(string relPath, XElement root)
        {
            // Estraggo gli elementi del percorso per scendere nell'albero dell'xml
            string[] pathElement = relPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            XElement ret = root;

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
        private static XElement getFileElement(string relPath, XElement root)
        {
            XElement ret = null;
            XElement dir = getDirectoryElement(Path.GetDirectoryName(relPath), root);
    
            ret = dir.Elements(FileElementName).Where(elt => elt.Attribute(FileAttributeName).Value == Path.GetFileName(relPath)).FirstOrDefault();

            return ret;
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

        /// <summary>
        /// Cerco l'elemento relativo al file passato e ritorno una struttura con le informazioni
        /// </summary>
        /// <param name="relPath"></param>
        /// <returns></returns>
        public VersionInfo GetVersionInfo(string relPath)
        {
            VersionInfo ret = null;

            XElement fileE = getFileElement(relPath, this.GetRoot());

            if(fileE != null)
            {

                ret = new VersionInfo()
                {
                    FileSize = Convert.ToInt64(fileE.Attribute(FileAttributeSize).Value),
                    LastModTime = DateTime.Parse(fileE.Attribute(FileAttributeLastModTime).Value),
                    versionID = 0, // Non ho la versione nell'xml locale
                    Md5 = fileE.Attribute(FileAttributeChecksum).Value
                };

            }

            return ret;
        }
        #endregion
        
        #region CHECK DIFF
        /// <summary>
        /// Controllo due XML rendendo la lista dei file che devono essere RICHIESTI al server
        /// </summary>
        /// <param name="xmlClient">Xml della situazione del client</param>
        /// <param name="xmlServer">Xml del server</param>
        /// <param name="diffList">Lista dove andare ad inserire i file da richiedere</param>
        /// <param name="deletedFilelist">Lista dei file cancellati del client per evitare di richiederli al server</param>
        /// <param name="curPath">Vuoto</param>
        public static void checkDiffClientServerTOClient(XElement xmlClient, XElement xmlServer, List<VersionInfo> diffList, List<string> deletedFilelist, string curPath = "")
        {
            bool getFileFromServer = true;

            #region FILE DIFF
            //Prendo le informazioni dagli xml per i FILE
            var serverFilesList = xmlServer.Elements(XmlManager.FileElementName);

            // Per ogni file sul server controllo di averlo uguale, altrimenti lo richiedo
            foreach (XElement serverFileVersionListElement in serverFilesList)
            {
                getFileFromServer = true;

                // Per ogni file del server controllo solo l'ultima versione
                VersionInfo serverFileInfo = XmlManager.GetFileLastVersionInfo(serverFileVersionListElement, curPath);

                // Controllo che non sia cancellato, altrimenti passo oltre
                if (serverFileInfo.deleted == true)
                    continue;

                // Se il file corrente è nella lista dei file cancellati passo oltre
                if (deletedFilelist.Contains(serverFileInfo.relPath) == true)
                    continue;

                // Recupero (se presente) l'elemento del file per il client
                XElement clientFileElement = XmlManager.getFileElement(Path.GetFileName(serverFileInfo.relPath), xmlClient);
                if(clientFileElement != null)
                {
                    VersionInfo clientFileInfo = XmlManager.GetFileInfo(clientFileElement, curPath);

                    //Controllo che il checksum sia uguale oppure che il mio file sia più recente
                    if(clientFileInfo.Md5.CompareTo(serverFileInfo.Md5) == 0 || DateTime.Compare(clientFileInfo.LastModTime, serverFileInfo.LastModTime) >= 0)
                        getFileFromServer = false;
                }

                if(getFileFromServer == true)
                {
                    // File nuovo sul server, sul client non ce l'ho e non è uno dei file che ho cancellato ora
                    // Lo aggiungo alla lista di file da richiedere
                    diffList.Add(serverFileInfo); 
                }
            }
            #endregion

            #region FOLDER DIFF
            // Prendo le informazioni dagli xml per le CARTELLE
            var serverSubDirList = xmlServer.Elements(XmlManager.DirectoryElementName);

            // Richiamo ricorsivamente per ogni sottocartella presente sul server
            foreach (XElement serverSubDirElement in serverSubDirList)
            {
                string serverSubDirRelPath = curPath + Constants.PathSeparator + serverSubDirElement.Attribute(XmlManager.FileAttributeName).Value;

                // Recupero l'elemento della cartella del client (se presente)
                XElement clientSubDirElement = XmlManager.getDirectoryElement(Path.GetFileName(serverSubDirRelPath), xmlClient);

                if(clientSubDirElement == null)
                {
                    // Nel client non è presente quella sotto cartella, tutti gli elementi del server vanno scaricati
                    XmlManager.getSubdirs(serverSubDirElement, diffList, serverSubDirRelPath, true, deletedFilelist);

                    // cancello tutti gli elementi che risultano Deleted dal server
                    diffList.RemoveAll(vInfo => vInfo.deleted == true);
                    
                }
                else
                {
                    // Sotto cartella presente, ricorro
                    XmlManager.checkDiffClientServerTOClient(clientSubDirElement, serverSubDirElement, diffList, deletedFilelist, serverSubDirRelPath);
                }
            }            
            #endregion
            
            return;
        }


        /// <summary>
        /// Controllo due XML rendendo la lista dei file che devono essere MANDATI al server
        /// </summary>
        /// <param name="xmlClient">Xml della situazione del client</param>
        /// <param name="xmlServer">Xml del server</param>
        /// <param name="diffList">Lista dove andare ad inserire i file da inviare</param>
        /// <param name="curPath">Vuoto</param>
        public static void checkDiffClientClientTOServer(XElement xmlClient, XElement xmlServer, List<VersionInfo> diffList, string curPath = "")
        {
            bool sendFileToServer = true;

            #region FILE DIFF
            //Prendo le informazioni dagli xml per i FILE
            var clientFiles = xmlClient.Elements(XmlManager.FileElementName);

            // Per ogni file sul client controllo di averlo uguale sul server, altrimenti lo mando
            foreach (XElement clientFileElement in clientFiles)
            {
                sendFileToServer = true;

                VersionInfo clientFileInfo = XmlManager.GetFileInfo(clientFileElement, curPath);

                // Recupero (se presente) l'elemento del file per il server
                XElement serverFileElement = XmlManager.getFileElement(Path.GetFileName(clientFileInfo.relPath), xmlServer);

                if(serverFileElement != null)
                {
                    List<VersionInfo> serverFileInfoList = XmlManager.GetFileVersionInfoList(serverFileElement, curPath, true);
                    VersionInfo serverFileLastVersionInfo = XmlManager.GetFileLastVersionInfo(serverFileElement, curPath);

                    // SE: lo stesso file è gia presente sul server (dimensione e md5 uguali in una delle versioni) NON INVIO
                    foreach (VersionInfo serverFileInfo in serverFileInfoList)
                    {
                        if (clientFileInfo.Md5.CompareTo(serverFileInfo.Md5) == 0 && clientFileInfo.FileSize == serverFileInfo.FileSize)
                        {
                            sendFileToServer = false;
                            break; // EVITO IL PING PONG DEI FILE DOPO UNA RESTORE!!!!!!!
                        }
                    }
                }

                if(sendFileToServer == true)
                {
                    // Siccome ho gia scaricato l'ultima versione in ordine temporale se arrivo qua sicuramente devo spedire il file
                    diffList.Add(clientFileInfo);
                }
            }
            #endregion

            #region FOLDER DIFF
            // Prendo le informazioni dagli xml per le CARTELLE
            var clientSubDirList = xmlClient.Elements(XmlManager.DirectoryElementName);

            // Richiamo ricorsivamente per ogni sottocartella presente sul server
            foreach (XElement clientSubDirElement in clientSubDirList)
            {
                string clientSubDirRelPath = curPath + Constants.PathSeparator + clientSubDirElement.Attribute(XmlManager.FileAttributeName).Value;

                // Recupero l'elemento della cartella del client (se presente)
                XElement serverSubDirElement = XmlManager.getDirectoryElement(Path.GetFileName(clientSubDirRelPath), xmlServer);

                if (serverSubDirElement == null)
                {
                    // Nel client non è presente quella sotto cartella, tutti gli elementi del server vanno scaricati
                    XmlManager.getSubdirs(clientSubDirElement, diffList, clientSubDirRelPath, false);
                }
                else
                {
                    // Sotto cartella presente, ricorro
                    XmlManager.checkDiffClientClientTOServer(clientSubDirElement, serverSubDirElement, diffList, clientSubDirRelPath);
                }
            }
            #endregion

            return;
        }

        /// <summary>
        /// funzione che elenca ricorsivamente tutti i files e tutte le cartelle presenti 
        /// all'interno della cartella interessata (root)
        /// </summary>
        /// <param name="root">XElement cartella interessata</param>
        /// <param name="refList">Lista degli elementi trovati</param>
        /// <param name="curPath">path temporaneo</param>
        /// <param name="versionElement">TRUE se sto analizzando XML del server (con versioni)</param>
        public static void getSubdirs(XElement root, List<VersionInfo> refList, string curPath, bool versionElement, List<string> deletedFileList = null)
        {
            var dirsList = root.Elements(XmlManager.DirectoryElementName);

            IEnumerable<XElement> filesList = null;
            //if (versionElement == true)
            //    filesList = root.Elements(XmlManager.VersionElementName);
            //else
                filesList = root.Elements(XmlManager.FileElementName);


            foreach (XElement fileElement in filesList)
            {

                VersionInfo newFileVersionInfo = null;

                if (versionElement == true)
                    newFileVersionInfo = XmlManager.GetFileLastVersionInfo(fileElement, curPath);
                else
                    newFileVersionInfo = XmlManager.GetFileInfo(fileElement, curPath);
                
                //Se il file è stato cancellato lo ignoro
                if (deletedFileList != null && deletedFileList.Contains(newFileVersionInfo.relPath) == true)
                    continue;

                refList.Add(newFileVersionInfo);
            }

            // Ricorro sulle sottocartelle
            foreach (XElement dirElement in dirsList)
            {
                string serverSubDirRelPath = curPath + Constants.PathSeparator + dirElement.Attribute(XmlManager.FileAttributeName).Value;
                getSubdirs(dirElement, refList, serverSubDirRelPath, versionElement, deletedFileList);
            }
            
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

            //string outPath = Constants.XmlSavePath + Constants.PathSeparator + "C_DIGEST.xml";
            //using (StreamWriter output = new StreamWriter(outPath))
            //{
            //    output.Write(doc.ToString());
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
            using(StreamWriter output = new StreamWriter(path))
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
        /// <summary>
        /// Ottengo la struttura con gli attributi da un FILE
        /// </summary>
        public static VersionInfo GetFileInfo(XElement fileElement, string curPath)
        {
            VersionInfo ret = new VersionInfo()
            {
                FileSize = -1,
                Md5 = "",
                relPath = "",
                versionID = -1
            };

            try
            {
                //Controllo se è un vero file (senza versioni)
                if (fileElement.Elements(XmlManager.VersionElementName).Count() > 0)
                    throw new Exception("GetFileInfo richiamata su elemento con versioni");

                ret.FileSize = Convert.ToInt32(fileElement.Attribute(XmlManager.FileAttributeSize).Value);
                ret.Md5 = fileElement.Attribute(XmlManager.FileAttributeChecksum).Value;
                ret.LastModTime = DateTime.Parse(fileElement.Attribute(XmlManager.FileAttributeLastModTime).Value);
                ret.relPath = curPath + Constants.PathSeparator + fileElement.Attribute(XmlManager.FileAttributeName).Value;
                
            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);
            }



            return ret;
        }

        /// <summary>
        /// Ottengo la struttura con gli attributi da una lista di versioni prendendo l'ultima (lastVersion = 1)
        /// </summary>
        public static VersionInfo GetFileLastVersionInfo(XElement fileElement, string curPath)
        {
            VersionInfo ret = new VersionInfo()
            {
                FileSize = -1,
                Md5 = "",
                relPath = "",
                versionID = -1,
                deleted = false
            };

            try
            {
                //Controllo se è un file con versioni
                if (fileElement.Elements(XmlManager.VersionElementName).Count() < 1)
                    throw new Exception("GetFileLastVersionInfo richiamata su elemento senza versioni");

                var versionList = fileElement.Elements(XmlManager.VersionElementName);

                foreach (var versionElement in versionList)
                {
                    // Controllo se è l'ultima versione
                    if(versionElement.Attribute(XmlManager.VersionAttributeLastVersion).Value.CompareTo("true") == 0)
                    {
                        ret.FileSize = Convert.ToInt32(versionElement.Attribute(XmlManager.FileAttributeSize).Value);
                        ret.Md5 = versionElement.Attribute(XmlManager.FileAttributeChecksum).Value;
                        ret.LastModTime = DateTime.Parse(versionElement.Attribute(XmlManager.FileAttributeLastModTime).Value);
                        ret.relPath = curPath + Constants.PathSeparator + fileElement.Attribute(XmlManager.FileAttributeName).Value;
                        ret.versionID = Convert.ToInt32(versionElement.Attribute(XmlManager.VersionAttributeID).Value);
                        ret.deleted = Convert.ToBoolean(versionElement.Attribute(XmlManager.VersionAttributeDeleted).Value);

                        return ret;
                    }
                }

            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);
            }



            return ret;
        }

        /// <summary>
        /// Ottengo la lista degli attributi di tutte le versioni
        /// </summary>
        public static List<VersionInfo> GetFileVersionInfoList(XElement fileElement, string curPath, bool sortLastModTime = false)
        {
            List<VersionInfo> ret = new List<VersionInfo>();

            try
            {
                //Controllo se è un file con versioni
                if (fileElement.Elements(XmlManager.VersionElementName).Count() < 1)
                    throw new Exception("GetFileLastVersionInfo richiamata su elemento senza versioni");

                var versionList = fileElement.Elements(XmlManager.VersionElementName);

                foreach (var versionElement in versionList)
                {
                    // Aggiungo tutte le versioni
                    VersionInfo vInfo = new VersionInfo()
                    {
                        FileSize = -1,
                        Md5 = "",
                        relPath = "",
                        versionID = -1,
                        deleted = false
                    };

                    vInfo.FileSize = Convert.ToInt32(versionElement.Attribute(XmlManager.FileAttributeSize).Value);
                    vInfo.Md5 = versionElement.Attribute(XmlManager.FileAttributeChecksum).Value;
                    vInfo.LastModTime = DateTime.Parse(versionElement.Attribute(XmlManager.FileAttributeLastModTime).Value);
                    vInfo.relPath = curPath + Constants.PathSeparator + fileElement.Attribute(XmlManager.FileAttributeName).Value;
                    vInfo.versionID = Convert.ToInt32(versionElement.Attribute(XmlManager.VersionAttributeID).Value);
                    vInfo.deleted = Convert.ToBoolean(versionElement.Attribute(XmlManager.VersionAttributeDeleted).Value);

                    ret.Add(vInfo);
                }
            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);
            }

            if(sortLastModTime)
                ret.Sort((a, b) => a.LastModTime.CompareTo(b.LastModTime)); // TODO TESTARE

            return ret;
        }



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
