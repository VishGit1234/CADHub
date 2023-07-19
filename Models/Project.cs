using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Threading;
using static CADHub.Models.FileChange;

namespace CADHub.Models
{
    class Project
    {
        private string rootFolderPath;
        public string getRootFolderPath() { return rootFolderPath; }

        public string ProjectName { get; private set; }

        private Dictionary<string, DateTime> writeTimes;
        public void UpdateWriteTime(string path, DateTime time)
        {
            writeTimes[path] = time;
        }
        public void UpdateWriteTime(string path)
        {
            writeTimes.Remove(path);
        }

        public ObservableCollection<FileChange> FileChanges { get; private set; }

        private Dictionary<string, FileChange> trackedFileChanges;
        public bool LocalFileChangeExists(string path, CHANGE_TYPES type) { return trackedFileChanges.ContainsKey(path + type); }

        public ObservableCollection<FileChange> RemoteFileChanges { get; private set; }

        /// <summary>
        /// Remote write times of files that have been synced locally
        /// </summary>
        private Dictionary<string, DateTime> remoteWriteTimes;
        public bool RemoteWriteTimeChangeExists(string path, DateTime serverWriteTime)
        {
            if (!remoteWriteTimes.ContainsKey(path)) return false;
            var temp = remoteWriteTimes[path].Ticks / 10000000;
            var temp2 = serverWriteTime.Ticks / 10000000;
            return remoteWriteTimes[path].Ticks/10000000 != serverWriteTime.Ticks/10000000;
        }
        public List<string> ContainsAllRemoteWriteTimeFiles(ref HashSet<string> filePaths)
        {
            List<string> lst = new List<string>();
            foreach (var path in remoteWriteTimes.Keys)
            {
                if (!filePaths.Contains(path)) lst.Add(path);
            }
            return lst;
        }
        public void UpdateRemoteWriteTimes(string path, DateTime time)
        {
            remoteWriteTimes[path] = time;
        }
        public void SaveRemoteWriteTimes()
        {
            IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
            try
            {
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_remote_write_times.txt", FileMode.Create, storage))
                {
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        // Write write times
                        foreach (var writeTime in remoteWriteTimes)
                        {
                            writer.WriteLine(writeTime.Key + "," + writeTime.Value.ToString());
                        }
                    }
                }
            }
            catch { }
        }


        /// <summary>
        /// Constructor for a project. Initialises the project and recursively scans 
        /// and stores the last write time and file path (for comparison usage)
        /// </summary>
        /// <param name="path"></param>
        public Project(string path)
        {
            rootFolderPath = path;
            ProjectName = Path.GetFileName(path)!;
            writeTimes = new Dictionary<string, DateTime>();
            FileChanges = new ObservableCollection<FileChange>();
            RemoteFileChanges = new ObservableCollection<FileChange>();
            trackedFileChanges = new Dictionary<string, FileChange>();
            remoteWriteTimes = new Dictionary<string, DateTime>();

            // Restore project information (if possible)
            RestoreProject();

            // Create thread to track local changes
            var localChangeTrackerThread = new Thread(LocalChanges);
            localChangeTrackerThread.Start();
        }

        public void PruneLists(ref List<FileChange> syncedFiles)
        {
            foreach (FileChange syncedFile in syncedFiles)
            {
                trackedFileChanges.Remove(syncedFile.FilePath + syncedFile.ChangeType);
                FileChanges.Remove(syncedFile);
            }
        }
        public void PruneLists(FileChange change)
        {
            trackedFileChanges.Remove(change.FilePath + change.ChangeType);
            FileChanges.Remove(change);
        }
        public void PruneRemoteLists(ref List<FileChange> mergedFiles)
        {
            foreach (FileChange mergedFile in mergedFiles)
            {
                RemoteFileChanges.Remove(mergedFile);
            }
        }

        private void RestoreProject()
        {
            IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
            try
            {
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_file_changes.txt", FileMode.Open, storage))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string? temp = reader.ReadLine();
                        if (temp != null)
                        {
                            rootFolderPath = temp;
                            ProjectName = reader.ReadLine()!;
                            while (!reader.EndOfStream)
                            {
                                string[] keyValue = reader.ReadLine()!.Split(new char[] { ',' });
                                var fileChange = new FileChange(
                                    (CHANGE_TYPES)Enum.Parse(typeof(CHANGE_TYPES), keyValue[1]),
                                    keyValue[0], ProjectName);
                                FileChanges.Add(fileChange);
                                trackedFileChanges.Add(keyValue[0] + keyValue[1], fileChange);
                            }
                        }
                    }
                }
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_write_times.txt", FileMode.Open, storage))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            string? line = reader.ReadLine();
                            if (line != null)
                            {
                                string[] keyValue = line.Split(new char[] { ',' });
                                writeTimes.Add(keyValue[0], DateTime.Parse(keyValue[1]));
                            }
                        }
                    }
                }
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_remote_write_times.txt", FileMode.Open, storage))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            string? line = reader.ReadLine();
                            if (line != null)
                            {
                                string[] keyValue = line.Split(new char[] { ',' });
                                remoteWriteTimes.Add(keyValue[0], DateTime.Parse(keyValue[1]));
                            }
                        }
                    }
                }
                // If files not backed up -> back 'em up (can't use isolated storage here so instead just copy to application folder)
                if (!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", ProjectName)))
                {
                    CopyDirectory(rootFolderPath, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", ProjectName), true);
                }
            }
            catch { }
        }

        public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        // time in ms to wait between local change checks
        const int LOCAL_CHANGE_CHECK_INTERVAL = 1000;

        public ManualResetEvent threadBlock { get; private set; } = new ManualResetEvent(true);

        private void LocalChanges()
        {
            while (1 == 1)
            {
                threadBlock.WaitOne();
                int numberOfDeletions = 0;
                // First check for file modifications or deletions
                foreach (var filePath in writeTimes.Keys)
                {
                    if (!File.Exists(filePath))
                    {
                        // If the file doesn't exist anymore then it has been deleted
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            if (!trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Deleted))
                            {
                                // If we have unpushed create/modify change on the same file we can remove that
                                if (trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Created))
                                {
                                    FileChanges.Remove(trackedFileChanges[filePath + CHANGE_TYPES.Created]);
                                    trackedFileChanges.Remove(filePath + CHANGE_TYPES.Created);
                                }
                                else if (trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Modified))
                                {
                                    FileChanges.Remove(trackedFileChanges[filePath + CHANGE_TYPES.Modified]);
                                    trackedFileChanges.Remove(filePath + CHANGE_TYPES.Modified);
                                }
                                else
                                {
                                    var fileChange = new FileChange(CHANGE_TYPES.Deleted, filePath, ProjectName);
                                    FileChanges.Add(fileChange);
                                    trackedFileChanges.Add(filePath + CHANGE_TYPES.Deleted, fileChange);
                                }
                            }
                        });
                        numberOfDeletions++;
                    }
                    else if (new FileInfo(filePath).LastWriteTimeUtc.Ticks/10000000 != writeTimes[filePath].Ticks/10000000)
                    {
                        // Write times don't match which means file has been modified
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            if (!trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Modified))
                            {
                                var fileChange = new FileChange(CHANGE_TYPES.Modified, filePath, ProjectName);
                                FileChanges.Add(fileChange);
                                trackedFileChanges.Add(filePath + CHANGE_TYPES.Modified, fileChange);
                            }
                        });
                    }
                }
                // Check for new files
                var tempFileWriteTimes = new Dictionary<string, DateTime>();
                RecurseDirectory(rootFolderPath, ref tempFileWriteTimes);
                foreach (var filePath in tempFileWriteTimes.Keys)
                {
                    if (!writeTimes.ContainsKey(filePath))
                    {
                        // New file has been created
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            if (!trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Created))
                            {
                                if (trackedFileChanges.ContainsKey(filePath + CHANGE_TYPES.Deleted))
                                {
                                    // File has been deleted then created (so basically modified)
                                    FileChanges.Remove(trackedFileChanges[filePath + CHANGE_TYPES.Deleted]);
                                    trackedFileChanges.Remove(filePath + CHANGE_TYPES.Deleted);
                                    var fileChange = new FileChange(CHANGE_TYPES.Modified, filePath, ProjectName);
                                    FileChanges.Add(fileChange);
                                    trackedFileChanges.Add(filePath + CHANGE_TYPES.Modified, fileChange);
                                }
                                else
                                {
                                    var fileChange = new FileChange(CHANGE_TYPES.Created, filePath, ProjectName);
                                    FileChanges.Add(fileChange);
                                    trackedFileChanges.Add(filePath + CHANGE_TYPES.Created, fileChange);
                                }
                            }
                        });
                    }
                }
                // Update write times dictionary
                writeTimes = tempFileWriteTimes.ToDictionary(entry => entry.Key, entry => entry.Value);

                // Save project information to file (so app can retrieve after being closed)
                IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_file_changes.txt", FileMode.Create, storage))
                {
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        writer.WriteLine(rootFolderPath);
                        writer.WriteLine(ProjectName);
                        // Write File Change information
                        foreach (var change in FileChanges)
                        {
                            writer.WriteLine(change.FilePath + "," + change.ChangeType);
                        }
                    }
                }
                using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(ProjectName + "_write_times.txt", FileMode.Create, storage))
                {
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        // Write write times
                        foreach (var writeTime in writeTimes)
                        {
                            writer.WriteLine(writeTime.Key + "," + writeTime.Value.ToString());
                        }
                    }
                }

                Thread.Sleep(LOCAL_CHANGE_CHECK_INTERVAL);
            }
        }

        /// <summary>
        /// Compares two files to check if there are any 
        /// differences between them
        /// </summary>
        /// <param name="file1"></param>
        /// <param name="file2"></param>
        /// <returns> True if files are the same. False if not </returns>
        private bool FileCompare(string file1, string file2)
        {
            int file1byte;
            int file2byte;
            FileStream fs1;
            FileStream fs2;

            // Determine if the same file was referenced two times.
            if (file1 == file2)
            {
                // Return true to indicate that the files are the same.
                return true;
            }

            // Open the two files.
            fs1 = new FileStream(file1, FileMode.Open, FileAccess.Read);
            fs2 = new FileStream(file2, FileMode.Open, FileAccess.Read);

            // Check the file sizes. If they are not the same, the files 
            // are not the same.
            if (fs1.Length != fs2.Length)
            {
                // Close the file
                fs1.Close();
                fs2.Close();

                // Return false to indicate files are different
                return false;
            }

            // Read and compare a byte from each file until either a
            // non-matching set of bytes is found or until the end of
            // file1 is reached.
            do
            {
                // Read one byte from each file.
                file1byte = fs1.ReadByte();
                file2byte = fs2.ReadByte();
            }
            while (file1byte == file2byte && file1byte != -1);

            // Close the files.
            fs1.Close();
            fs2.Close();

            // Return the success of the comparison. "file1byte" is 
            // equal to "file2byte" at this point only if the files are 
            // the same.
            return file1byte - file2byte == 0;
        }

        private static bool CheckIfVirtual(string path)
        {
            try { var temp = Directory.GetFiles(path); } catch { return true; }
            return false;
        }

        private void RecurseDirectory(string path, ref Dictionary<string, DateTime> fileWriteTimes)
        {
            foreach (var filePath in Directory.GetFiles(path))
            {
                fileWriteTimes.Add(filePath, new FileInfo(filePath).LastWriteTimeUtc);
            }
            foreach (var folderPath in Directory.GetDirectories(path))
            {
                if (!CheckIfVirtual(folderPath))
                    RecurseDirectory(folderPath, ref fileWriteTimes);
            }
        }
    }

    class FileChange : ModelBaseClass
    {
        public enum CHANGE_TYPES { Modified, Deleted, Created };

        public string FilePath { get; private set; }

        public string SimplifiedFilePath { get; private set; }

        public CHANGE_TYPES ChangeType { get; private set; }

        public bool isSelected { get; set; }

        private int progressBar;
        public int ProgressBar
        {
            get { return progressBar; }
            set { progressBar = value; OnPropertyChanged(nameof(ProgressBar)); }
        }

        public FileChange(CHANGE_TYPES changeType, string filePath, string projectName)
        {
            FilePath = filePath;
            ChangeType = changeType;
            SimplifiedFilePath = filePath.Substring(filePath.IndexOf(projectName));
            isSelected = true;
            ProgressBar = 0;
        }

    }

}



