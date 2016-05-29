using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;


namespace Client
{
    class XmlManager
    {
        #region NOMI DEI CAMPI
        // TODO: mettere per ogni valore il commento ///
        public static readonly string DirectoryElementName = "dir";
        public static readonly string DirectoryAttributeName = "name";

        public static readonly string FileElementName = "file";
        public static readonly string FileAttributeName = "name";
        public static readonly string FileAttributeLastModTime = "modTime";
        public static readonly string FileAttributeSize = "dim";
        public static readonly string FileAttributeChecksum = "md5";
        public static readonly string FileAttributeVersion = "id";
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

            XElement dirElement = this.getDirectoryElement(oldRelPath);
            
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

            XElement fileElement = this.getFileElement(oldRelPath);
            
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
            XElement fileElement = this.getFileElement(fa.RelFilePath);
            
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
            XElement el = this.getDirectoryElement(path);

            // Controllo se ho effettivamente trovato l'elemento, se è null allora significa che è un file
            if (el == null)
            {
                el = this.getFileElement(path);
                deletedFiles.Add(path);
            }
            else
            {
                // Richiamo sulla cartella
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
            XElement dir = this.getDirectoryElement(Path.GetDirectoryName(fa.RelFilePath));

            // Creo l'oggetto da inserire
            XElement file = new XElement(FileElementName);

            // Aggiungo gli attributi
            file.SetAttributeValue(FileAttributeName, Path.GetFileName(fa.RelFilePath));
            file.SetAttributeValue(FileAttributeLastModTime, fa.LastModtime.ToString(Constants.XmlDateFormat));
            file.SetAttributeValue(FileAttributeSize, fa.Size.ToString());
            file.SetAttributeValue(FileAttributeChecksum, fa.Md5);

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
            DirectoryInfo dirInfo = new DirectoryInfo(absPath);

            XElement parentDirElem = this.getDirectoryElement(parentDir);

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
        private XElement getDirectoryElement(string relPath)
        {
            // Estraggo gli elementi del percorso per scendere nell'albero dell'xml
            string[] pathElement = relPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            XElement ret = this.GetRoot();

            // prendo il FIRST dell'array ritornato dalla where perchè il filesystem mi garantisce l'univocità dei nomi
            // degli oggetti (directory o file) presenti in una directory
            foreach (string elem in pathElement)
                ret = ret.Elements(DirectoryElementName).Where(elt => elt.Attribute(DirectoryAttributeName).Value == elem).FirstOrDefault();
            
            return ret;
        }

        /// <summary>
        /// Cerca nel documento corrente l'elemento corrispondente al file il cui percorso è passato come parametro </summary>
        /// <param name="relPath">Percorso RELATIVO alla cartella per cui è stato generato il documento</param>
        /// <returns>L'elemento XML corrispondente</returns>
        private XElement getFileElement(string relPath)
        {
            XElement ret = null;
            XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath));
    
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

            XElement fileE = this.getFileElement(relPath);

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

        #region CONFRONTO XML
        /// <summary>
        /// controlla due XML rendendo l'elenco dei files che sono stati aggiunti/modificati
        /// l'elenco viene memorizzato in res (stringa che deve essere passata vuota)
        /// </summary>
        /// <param name="rc">XElement del client </param>
        /// <param name="rs">XElement del server </param>
        /// <param name="res">stringa (inizializzata vuota) che memorizza l'elenco dei path dei file diversi</param>
        /// <param name="path">stringa per memorizzare il path di ciascun file differente</param>
        public static int checkDiff(XElement rc, XElement rs, List<String> refList, ref string res, string path)
        {
            int counter = 0;
            bool presente = false;
            bool uguali = false;
            bool cartellaesistente = false;


            var filesC = from e in rc.Elements(FileElementName)
                         select e;

            var filesS = from eS in rs.Elements(FileElementName)
                         select eS;

            var subdirsC = from cc in rc.Elements(DirectoryElementName)
                           select cc;

            var subdirsS = from cs in rs.Elements(DirectoryElementName)
                           select cs;


            foreach (XElement e in filesC)
            {
                ///cerco se e (cioè il file i-esimo) sia presente nella cartella considerata
                presente = false;
                uguali = false;
                foreach (XElement eS in filesS)
                {
                    if (e.Attribute("name").ToString().CompareTo(eS.Attribute("name").ToString()) == 0)
                    {
                        //nome presente
                        if (e.Attribute("md5").ToString().CompareTo(eS.Attribute("md5").ToString()) == 0)
                        {
                            //file identico in nome e in checksum
                            uguali = true;
                        }
                        presente = true;
                    }
                }

                if (presente == false)
                {
                    counter++;
                    res += "PUSH" + path + "\\" + e.Attribute("name").Value + "\n";
                    refList.Add(path + "\\" + e.Attribute("name").Value);
                }
                else if (uguali == false)
                {
                    counter++;
                    res += "PUSH* " + path + "\\" + e.Attribute("name").Value + "\n";
                    refList.Add(path + "\\" + e.Attribute("name").Value);
                }
            }

            ///chiamo ricorsivamente la funzione per ogni sottocartella che sia presente anche lato server
            foreach (XElement cc in subdirsC)
            {
                cartellaesistente = false;
                foreach (XElement cs in subdirsS)
                {
                    if (cc.Attribute("name").ToString().CompareTo(cs.Attribute("name").ToString()) == 0)
                    {
                        //cartella esistente, ricorro
                        cartellaesistente = true;
                        counter += checkDiff(cc, cs, refList, ref res, path + "\\" + cc.Attribute("name").Value);
                    }
                }
                if (cartellaesistente == false)
                {
                    //cartella inesistente, stampo e NON RICORRO
                    //res += ("cartella NUOVA " + path + "\\" + cc.Attribute("name").Value + "\n");

                    //TO DO cercare una funzione che renda il path completo dato un XElement, metterla come stringa di partenza al posto di path
                    counter += getSubdirs(cc, refList, ref res, path + "\\" + cc.Attribute("name").Value);
                }
            }

            return counter;
        }

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
