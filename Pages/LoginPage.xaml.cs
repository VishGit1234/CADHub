using CADHub.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Themes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CADHub.Pages
{
    /// <summary>
    /// Interaction logic for LoginPage.xaml
    /// </summary>
    public partial class LoginPage : Page
    {
        public EventHandler? DoneLogin = null;
        
        public static string Token { get; private set; } = string.Empty;

        public LoginPage()
        {
            InitializeComponent();
            InitializeWebView();
        }

        [Serializable]
        private class AccessTokenClass
        {
            public string? access_token { get; set; }
        }



        private async void InitializeWebView()
        {
            await loginWebView.EnsureCoreWebView2Async(null);
            //var clearTask = loginWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();

            byte[] encrypted = File.ReadAllBytes(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test2.bin"));
            string CLIENT_ID = MainApplicationViewModel.DecryptString(encrypted, "erfit89shdFerGwe", "aw9dHdfi78E3Fer3");
            encrypted = File.ReadAllBytes(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test3.bin"));
            string CLIENT_SECRET = MainApplicationViewModel.DecryptString(encrypted, "erfit89shdFerGwe", "aw9dHdfi78E3Fer3");

            // Now WebView2 is initialized and ready to be used.
            loginWebView.CoreWebView2.Navigate("https://cadhub.auth.ca-central-1.amazoncognito.com/oauth2/authorize?client_id=" + CLIENT_ID + "&response_type=code&scope=aws.cognito.signin.user.admin&redirect_uri=http://localhost");
            loginWebView.CoreWebView2.NavigationStarting += (sender, e) =>
            {
                if (!e.Uri.StartsWith("http://localhost"))
                    return;

                //get code parameter from redirect uri  
                var uri = new Uri(e.Uri);
                var code = HttpUtility.ParseQueryString(uri.Query).Get("code");

                HttpClient client = new HttpClient();

                //request token using authorization code 
                string authorization = "Basic " + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CLIENT_ID + ":" + CLIENT_SECRET));
                client.DefaultRequestHeaders.Add("Authorization", authorization);
                StringContent content = new StringContent("grant_type=authorization_code&client_id=" + CLIENT_ID + "&code=" + code + "&redirect_uri=http://localhost" + "&client_secret=" + CLIENT_SECRET, Encoding.UTF8, "application/x-www-form-urlencoded");
                var responseTask = client.PostAsync("https://cadhub.auth.ca-central-1.amazoncognito.com/oauth2/token", content);
                responseTask.Wait();
                if (responseTask.Result.IsSuccessStatusCode)
                {
                    // Get token
                    var readTask = responseTask.Result.Content.ReadAsStringAsync();
                    readTask.Wait();
                    Token = JsonSerializer.Deserialize<AccessTokenClass>(readTask.Result)!.access_token!;
                    DoneLogin?.Invoke(this, new EventArgs());
                }
                else
                {
                    Application.Current.Shutdown();
                }
            };
        }
    }
}
