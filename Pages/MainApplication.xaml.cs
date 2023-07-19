using CADHub.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CADHub
{
    /// <summary>
    /// Interaction logic for MainApplication.xaml
    /// </summary>
    public partial class MainApplication : Page
    {
        private MainApplicationViewModel viewModel;

        public MainApplication()
        {
            InitializeComponent();
            viewModel = new MainApplicationViewModel();
            DataContext = viewModel;
        }

        private void AddProject(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderPicker();
            dlg.InputPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (dlg.ShowDialog() == true)
            {
                viewModel.AddProject(dlg.ResultPath!);
            }
        }

        private void PushChanges(object sender, RoutedEventArgs e)
        {
            Task.Factory.StartNew(() => viewModel.PushChanges());
        }

        private void FetchChanges(object sender, RoutedEventArgs e)
        {
            Task.Run(() => viewModel.FetchChanges());
        }

        private void MergeChanges(object sender, RoutedEventArgs e)
        {
            Task.Factory.StartNew(() => { viewModel.MergeChanges(); });
        }

        private void FixChanges(object sender, RoutedEventArgs e)
        {
            viewModel.FixChanges();
        }

        private void DeleteProject(object sender, RoutedEventArgs e)
        {
            object project = (sender as Button)!.DataContext;
            viewModel.RemoveProject(ref project);
        }
    }
}



