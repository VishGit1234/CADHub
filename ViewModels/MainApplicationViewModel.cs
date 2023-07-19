using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using CADHub.Models;
using System.Security.Cryptography;

namespace CADHub.ViewModels
{
    class MainApplicationViewModel : ViewModelBaseClass
    {
        public ObservableCollection<Project> ConnectedProjects { get; private set; }

        private Project? selectedProject = null;

        public Project? SelectedProject
        {
            get { return selectedProject; }
            set { selectedProject = value; OnPropertyChanged(nameof(SelectedProject)); }
        }

        public MainApplicationViewModel()
        {
            ConnectedProjects = new ObservableCollection<Project>();
            byte[] encrypted = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test.bin"));
            LAMBDA_URL = DecryptString(encrypted, "erfit89shdFerGwe", "aw9dHdfi78E3Fer3");
            RestoreProjects();
        }

        private void RestoreProjects()
        {
            IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
            foreach (string fileName in storage.GetFileNames())
            {
                if (fileName.Contains("_file_changes"))
                {
                    string? projectFolder = null;
                    using (IsolatedStorageFileStream stream = new IsolatedStorageFileStream(fileName, FileMode.Open, storage))
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            projectFolder = reader.ReadLine();
                        }
                    }
                    if (projectFolder != null)
                        ConnectedProjects.Add(new Project(projectFolder));
                }
            }
        }

        public void AddProject(string path)
        {
            var newProject = new Project(path);
            foreach (var project in ConnectedProjects)
            {
                if (project.ProjectName == newProject.ProjectName) return;
            }
            ConnectedProjects.Add(newProject);
        }

        public void RemoveProject(ref object project)
        {
            if (SelectedProject != null)
                if (SelectedProject.Equals(project)) SelectedProject = null;
            ConnectedProjects.Remove((Project)project!);
        }

        private static byte[] EncryptString(string plainText, string key, string iv)
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            var check = Encoding.UTF8.GetBytes(iv);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            return aes.CreateEncryptor().TransformFinalBlock(Encoding.UTF8.GetBytes(plainText), 0, plainText.Length);
        }
        private static string DecryptString(byte[] cipherText, string key, string iv)
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = Encoding.UTF8.GetBytes(iv);
            return Encoding.UTF8.GetString(aes.CreateDecryptor().TransformFinalBlock(cipherText, 0, cipherText.Length));
        }

        readonly string LAMBDA_URL;

        private static readonly HttpClient client = new HttpClient();

        public async void MergeChanges()
        {
            if (SelectedProject != null)
            {
                var mergeFiles = new List<FileChange>(SelectedProject.RemoteFileChanges.Count);
                await Task.Factory.StartNew(() =>
                {
                    var files = new List<FileChange>(SelectedProject.RemoteFileChanges.Count);
                    foreach (FileChange change in SelectedProject.RemoteFileChanges)
                    {
                        files.Add(change);
                    }
                    foreach (FileChange change in files)
                    {
                        if (change.isSelected)
                        {
                            Task.Factory.StartNew(() =>
                            {
                                if (change.ChangeType == FileChange.CHANGE_TYPES.Created || change.ChangeType == FileChange.CHANGE_TYPES.Modified)
                                {
                                    // Get the presigned URL by invoking AWS lambda function
                                    JsonObject jsonObject = new JsonObject();
                                    jsonObject.Add("updateType", "download");
                                    jsonObject.Add("objectKey", change.SimplifiedFilePath.Replace('\\', '/'));

                                    StringContent content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                                    var responseTask = client.PostAsync(LAMBDA_URL, content);
                                    responseTask.Wait();
                                    var preSignedUrlTask = responseTask.Result.Content.ReadAsStringAsync();
                                    preSignedUrlTask.Wait();
                                    string preSignedUrl = preSignedUrlTask.Result;

                                    // Use presigned url to download file
                                    HttpResponseMessage getResponse;
                                    try
                                    {
                                        var getResponseTask = client.GetAsync(preSignedUrl);
                                        getResponseTask.Wait();
                                        getResponse = getResponseTask.Result;
                                        if (getResponse.IsSuccessStatusCode)
                                        {
                                            // Pause local change tracker
                                            SelectedProject.threadBlock.Reset();
                                            Directory.CreateDirectory(Path.GetDirectoryName(change.FilePath)!);
                                            using (var fs = new FileStream(change.FilePath, FileMode.OpenOrCreate))
                                            {
                                                var fileDownloadTask = getResponse.Content.CopyToAsync(fs);
                                                while (!fileDownloadTask.IsCompleted)
                                                {
                                                    int pos = (int)Math.Round(30 * (fs.Position / (double)fs.Length));
                                                    App.Current.Dispatcher.Invoke(() => { change.ProgressBar = pos; });
                                                }
                                                fileDownloadTask.Wait();
                                            }
                                            // Update the writeTimeTracker so the change doesn't get duplicated
                                            SelectedProject.UpdateWriteTime(change.FilePath, new FileInfo(change.FilePath).LastWriteTimeUtc);
                                            // Unpause local change tracker
                                            SelectedProject.threadBlock.Set();
                                        }
                                        else MessageBox.Show(getResponse.StatusCode.ToString());
                                    }
                                    catch { }
                                }
                                else
                                {
                                    // Delete the file on local
                                    try
                                    {
                                        // Pause local change tracker
                                        SelectedProject.threadBlock.Reset();
                                        File.Delete(change.FilePath);
                                        // Update the writeTimeTracker so the change doesn't get duplicated
                                        SelectedProject.UpdateWriteTime(change.FilePath);
                                        // Unpause local change tracker
                                        SelectedProject.threadBlock.Set();
                                    }
                                    catch { }
                                }
                                mergeFiles.Add(change);
                            }, TaskCreationOptions.AttachedToParent);
                        }
                    }
                });
                //if (mergeFiles.Count > 0)
                //    MessageBox.Show("Merged");
                // Update remoteTimeTracker to stop it from appearing again on app restart
                UpdateRemoteWriteTimes(ref mergeFiles);
                App.Current.Dispatcher.Invoke(() => { 
                    SelectedProject.PruneRemoteLists(ref mergeFiles);
                });
            }
        }

        private void UpdateRemoteWriteTimes(ref List<FileChange> changes)
        {
            if (SelectedProject != null)
            {
                // update remote write times

                // Get the presigned URL by invoking AWS lambda function
                JsonObject jsonObject2 = new JsonObject();
                jsonObject2.Add("updateType", "fetch");
                jsonObject2.Add("objectKey", SelectedProject.ProjectName);

                StringContent content2 = new StringContent(jsonObject2.ToString(), Encoding.UTF8, "application/json");
                var response2Task = client.PostAsync(LAMBDA_URL, content2);
                response2Task.Wait();
                var response2 = response2Task.Result;
                var preSignedUrl2Task = response2.Content.ReadAsStringAsync();
                preSignedUrl2Task.Wait();
                var preSignedUrl2 = preSignedUrl2Task.Result;

                // Use presigned url to get file information
                var fetchResponseTask = client.GetAsync(preSignedUrl2);
                fetchResponseTask.Wait();
                var fetchResponse = fetchResponseTask.Result;
                if (fetchResponse.IsSuccessStatusCode)
                {
                    var responseContentTask = fetchResponse.Content.ReadAsStringAsync();
                    responseContentTask.Wait();
                    var responseContentString = responseContentTask.Result;
                    HashSet<string> allFilePaths = new HashSet<string>();
                    if (!String.IsNullOrEmpty(responseContentString))
                    {
                        XDocument xmlContent = XDocument.Parse(responseContentString);
                        XNamespace ns = "http://s3.amazonaws.com/doc/2006-03-01/";
                        var filePaths = from i in xmlContent.Descendants(ns + "Contents")
                                        where !String.IsNullOrWhiteSpace(i.Element(ns + "Key")!.Value)
                                        select new KeyValuePair<string, DateTime>(i.Element(ns + "Key")!.Value,
                                        DateTime.Parse(i.Element(ns + "LastModified")!.Value));
                        foreach (var filePath in filePaths)
                        {
                            var fullPath = SelectedProject.getRootFolderPath()
                                + "\\" + filePath.Key.Remove(0, SelectedProject.ProjectName.Length + 1).Replace("/", "\\");
                            foreach(var change in changes)
                            {
                                if (change.FilePath == fullPath)
                                {
                                    SelectedProject.UpdateRemoteWriteTimes(fullPath, filePath.Value);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        public async void FetchChanges()
        {
            if (SelectedProject != null)
            {
                // Get the presigned URL by invoking AWS lambda function
                JsonObject jsonObject = new JsonObject();
                jsonObject.Add("updateType", "fetch");
                jsonObject.Add("objectKey", SelectedProject.ProjectName);

                StringContent content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(LAMBDA_URL, content);
                string preSignedUrl = await response.Content.ReadAsStringAsync();

                // Use presigned url to get all file paths
                HttpResponseMessage fetchResponse = await client.GetAsync(preSignedUrl);
                if (fetchResponse.IsSuccessStatusCode)
                {
                    var responseContentString = await fetchResponse.Content.ReadAsStringAsync();
                    HashSet<string> allFilePaths = new HashSet<string>();
                    if (!String.IsNullOrEmpty(responseContentString))
                    {
                        XDocument xmlContent = XDocument.Parse(responseContentString);
                        XNamespace ns = "http://s3.amazonaws.com/doc/2006-03-01/";
                        var filePaths = from i in xmlContent.Descendants(ns + "Contents")
                                        where !String.IsNullOrWhiteSpace(i.Element(ns + "Key")!.Value)
                                        select new KeyValuePair<string, DateTime>(i.Element(ns + "Key")!.Value,
                                        DateTime.Parse(i.Element(ns + "LastModified")!.Value));
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            SelectedProject.RemoteFileChanges.Clear();
                        });
                        foreach (var filePath in filePaths)
                        {
                            var fullPath = SelectedProject.getRootFolderPath()
                                + "\\" + filePath.Key.Remove(0, SelectedProject.ProjectName.Length + 1).Replace("/", "\\");
                            FileChange.CHANGE_TYPES? changeType = null;
                            if (!File.Exists(fullPath) && !SelectedProject.LocalFileChangeExists(fullPath, FileChange.CHANGE_TYPES.Deleted))
                            {
                                // File exists remote but not on local (file created on remote) and has not been deleted locally
                                changeType = FileChange.CHANGE_TYPES.Created;
                            }
                            else if (SelectedProject.RemoteWriteTimeChangeExists(fullPath, filePath.Value))
                            {
                                // Write times don't match (file has been modified on remote)
                                changeType = FileChange.CHANGE_TYPES.Modified;
                            }
                            if (changeType != null)
                            {
                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    SelectedProject.RemoteFileChanges.Add(new FileChange(
                                        changeType.Value, fullPath, SelectedProject.ProjectName));
                                });
                            }
                            allFilePaths.Add(fullPath);
                        }
                        // When a file is deleted on remote, we don't care unless we already pushed the same file
                        // We can check that by checking if each file in remoteWriteTimes is still in remote
                        foreach (var path in SelectedProject.ContainsAllRemoteWriteTimeFiles(ref allFilePaths))
                        {
                            // Check if already deleted on local
                            if (File.Exists(path))
                            {
                                // Deleted on remote
                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    SelectedProject.RemoteFileChanges.Add(new FileChange(
                                        FileChange.CHANGE_TYPES.Deleted, path, SelectedProject.ProjectName));
                                });
                            }
                        }
                    }
                    //MessageBox.Show("Fetched");
                }
                else MessageBox.Show(fetchResponse.StatusCode.ToString());
            }
        }

        public async void PushChanges()
        {
            if (SelectedProject != null)
            {
                // Pause local change tracker
                SelectedProject.threadBlock.Reset();
                var syncedFiles = new List<FileChange>(SelectedProject.FileChanges.Count);
                await Task.Factory.StartNew(() =>
                {
                    List<FileChange> changes = new List<FileChange>(SelectedProject.FileChanges.Count);
                    foreach(var fileChange in SelectedProject.FileChanges)
                    {
                        changes.Add(fileChange);
                    }
                    foreach (FileChange change in changes)
                    {
                        if (change.isSelected)
                        {
                           Task.Factory.StartNew(() =>
                            {
                                if (change.ChangeType == FileChange.CHANGE_TYPES.Created || change.ChangeType == FileChange.CHANGE_TYPES.Modified)
                                {
                                    // Get the presigned URL by invoking AWS lambda function
                                    JsonObject jsonObject = new JsonObject();
                                    jsonObject.Add("updateType", "upload");
                                    jsonObject.Add("objectKey", change.SimplifiedFilePath.Replace('\\', '/'));

                                    StringContent content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                                    var response = client.PostAsync(LAMBDA_URL, content);
                                    response.Wait();
                                    var preSignedUrlTask = response.Result.Content.ReadAsStringAsync();
                                    string preSignedUrl = preSignedUrlTask.Result;

                                    // Use presigned url to upload file
                                    HttpResponseMessage putResponse;
                                    try
                                    {
                                        using (Stream fileStream = File.OpenRead(change.FilePath))
                                        {
                                            var putTask = client.PutAsync(preSignedUrl, new StreamContent(fileStream));
                                            while (!putTask.IsCompleted)
                                            {
                                                int pos = (int)Math.Round(30 * (fileStream.Position / (double)fileStream.Length));
                                                App.Current.Dispatcher.Invoke(() => { change.ProgressBar = pos; });
                                            }
                                            putResponse = putTask.Result;
                                        }
                                        if (putResponse.IsSuccessStatusCode)
                                        {}
                                        else MessageBox.Show(putResponse.StatusCode.ToString());
                                    }
                                    catch { }
                                }
                                else // Delete operation
                                {
                                    // Get the presigned URL by invoking AWS lambda function
                                    JsonObject jsonObject = new JsonObject();
                                    jsonObject.Add("updateType", "delete");
                                    jsonObject.Add("objectKey", change.SimplifiedFilePath.Replace('\\', '/'));

                                    StringContent content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
                                    var response = client.PostAsync(LAMBDA_URL, content);
                                    response.Wait();
                                    var preSignedUrlTask = response.Result.Content.ReadAsStringAsync();
                                    var preSignedUrl = preSignedUrlTask.Result;

                                    // Use presigned url to delete file
                                    var deleteResponseTask = client.DeleteAsync(preSignedUrl);
                                    var deleteResponse = deleteResponseTask.Result;
                                    if (deleteResponse.IsSuccessStatusCode) { }
                                    else MessageBox.Show(deleteResponse.StatusCode.ToString());
                                }
                                syncedFiles.Add(change);
                            }, TaskCreationOptions.AttachedToParent);
                        }
                    }
                });

                //if(syncedFiles.Count > 0)
                //    MessageBox.Show("Pushed");
                UpdateRemoteWriteTimes(ref syncedFiles);
                SelectedProject.SaveRemoteWriteTimes();
                App.Current.Dispatcher.Invoke(() =>
                { 
                    SelectedProject.PruneLists(ref syncedFiles);
                });

                // Update backup
                try
                {
                    Project.CopyDirectory(SelectedProject.getRootFolderPath(), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", SelectedProject.ProjectName), true);
                }
                catch { }
                // Unpause local change tracker
                SelectedProject.threadBlock.Set();
            }
        }

        public void FixChanges()
        {
            if (SelectedProject != null)
            {
                // Pause local change tracker
                SelectedProject.threadBlock.Reset();
                var restoredFiles = new List<FileChange>(SelectedProject.FileChanges.Count);
                foreach (FileChange change in SelectedProject.FileChanges)
                {
                    if (change.isSelected)
                    {
                        try
                        {
                            if (change.ChangeType == FileChange.CHANGE_TYPES.Modified)
                            {
                                // Simply delete the file and copy the one from backup
                                File.Delete(change.FilePath);
                                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", change.SimplifiedFilePath), change.FilePath);
                                SelectedProject.UpdateWriteTime(change.FilePath, new FileInfo(change.FilePath).LastWriteTimeUtc);
                            }
                            else if (change.ChangeType == FileChange.CHANGE_TYPES.Deleted)
                            {
                                // Copy one from backup
                                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", change.SimplifiedFilePath), change.FilePath);
                                SelectedProject.UpdateWriteTime(change.FilePath, new FileInfo(change.FilePath).LastWriteTimeUtc);
                            }
                            else
                            {
                                // Delete it!
                                File.Delete(change.FilePath);
                                SelectedProject.UpdateWriteTime(change.FilePath);
                            }
                            restoredFiles.Add(change);
                        }
                        catch { }
                    }
                }
                SelectedProject.PruneLists(ref restoredFiles);
                // Unpause local change tracker
                SelectedProject.threadBlock.Set();
            }
        }
    }
}