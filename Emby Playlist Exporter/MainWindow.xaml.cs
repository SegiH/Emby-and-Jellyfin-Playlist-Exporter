using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ookii.Dialogs.Wpf;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;
using Serilog;
using Jellyfin_Playlist_Exporter.Models;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Navigation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Linq;

namespace EmbyJellyfin_Playlist_Exporter
{
    public partial class MainWindow : Window {
        readonly List<string> firstElements = new List<string> { "lblURL", "txtURL", "lblAPIKey", "txtAPIKey", "btnConnect", "menu" };
        readonly List<string> secondElements = new List<string> { "lblUserAccount", "lstUserAccounts" };
        readonly List<string> remainingElements = new List<string> { "btnSaveLocation", "chkSelectAllNone", "lstPlaylists", "lblSaveLocation", "txtSaveLocation" };
        readonly List<ElementModel> elementsProperties = new List<ElementModel> { };

        Playlists allPlaylists;
        bool isAdding = false;
        bool isAdjusted = false;
        readonly bool isLoading = false;
        readonly bool selectAllNone = false;
        readonly private Dictionary<string, string> allUserAccounts = new Dictionary<string, string>(); // Holds all user account data
        Thread loadPlaylistsThread;
        readonly ILogger<MainWindow> logger;
        readonly int topAdjustment = 40;
        readonly bool demoMode = false;
        List<string> embyDemoPlaylists = new List<string> { "90s", "Alternative", "Death Metal", "Rock" };
        List<string> jellyfinDemoPlaylists = new List<string> { "Classic Rock", "Random", "Punk", "80s" };

        public MainWindow() {
            InitializeComponent();

            InitElementProperties();

            Log.Logger = new LoggerConfiguration()
            .WriteTo.File("ejpe.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog();  // Add Serilog to the logger factory
            });

            logger = loggerFactory.CreateLogger<MainWindow>();

            isLoading = true;

            RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.HideAll.ToString());

            // Load saved settings
            if (!Properties.Settings.Default.URL.Equals("")) {
                txtURL.Text = Properties.Settings.Default.URL;
                txtAPIKey.Password = Properties.Settings.Default.APIKey;
                txtSaveLocation.Text = Properties.Settings.Default.SaveLocation;

                lstServerType.SelectedItem = Properties.Settings.Default.ServerType;

                if (Properties.Settings.Default.ServerType == ServerTypes.Emby)
                {
                    lblUserAccount.Visibility = Visibility.Hidden;
                    lstUserAccounts.Visibility = Visibility.Hidden;
                    btnConnect.Visibility = Visibility.Hidden;
                }

                if (!Properties.Settings.Default.UserAccount.Equals("") && !Properties.Settings.Default.UserAccount.Equals("-1")) {
                    LoadUserAccounts();

                    // Prevent lstUserAccounts from triggering LoadPlaylistValidation when adding items
                    isAdding = true;
                    lstUserAccounts.SelectedItem = Properties.Settings.Default.UserAccount;
                    isAdding = false;

                    LoadPlaylistValidation();
                }
            } else {
                // Use the desktop path as the default save location
                txtSaveLocation.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            };

            lstServerType.Items.Add("");
            lstServerType.Items.Add(ServerTypes.Emby);
            lstServerType.Items.Add(ServerTypes.Jellyfin);

            lstServerType.SelectedIndex = -1;

            isLoading = false;

            CheckForUpdates();
        }

        private void AdjustElementsTop()
        {
            string serverType = ServerType();

            if (serverType != ServerTypes.Jellyfin && !isAdjusted)
            {
                AdjustTop("lblSaveLocation", topAdjustment * -1);
                AdjustTop("txtSaveLocation", topAdjustment * -1);
                AdjustTop("btnSaveLocation", topAdjustment * -1);
                AdjustTop("chkSelectAllNone", topAdjustment * -1);
                AdjustTop("lstPlaylists", topAdjustment * -1);

                isAdjusted = true;
            }
            else if (serverType == ServerTypes.Jellyfin && isAdjusted)
            {
                AdjustTop("lblSaveLocation", topAdjustment);
                AdjustTop("txtSaveLocation", topAdjustment);
                AdjustTop("btnSaveLocation", topAdjustment);
                AdjustTop("chkSelectAllNone", topAdjustment);
                AdjustTop("lstPlaylists", topAdjustment);

                isAdjusted = false;
            }
        }

        private void AdjustTop(string elementName, int topAdjustmentAmount)
        {
            var el = FindName(elementName) as FrameworkElement;

            if (el != null)
            {
                var currentMargin = el.Margin;
                el.Margin = new System.Windows.Thickness(currentMargin.Left, currentMargin.Top + topAdjustmentAmount, currentMargin.Right, currentMargin.Bottom);
            }
        }

        // Event when the user clicks on the button to choose the save location
        private void BtnChooseSaveLocation_Click(object sender, EventArgs e) {
            VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog {
                Description = "Please select a folder.",
                UseDescriptionForTitle = true
            };

            bool dirSelected = (bool)dialog.ShowDialog(this);

            if (dirSelected == true) {
                txtSaveLocation.Text = dialog.SelectedPath;
            }
        }

        // Event when user clicks on Export Playlist
        private async void BtnExportPlaylists_Click(object sender, EventArgs e) {
            // Validate that a save location was selected. Since the save location field is read only and the app sets the desktop as the default location when the form loads, this shouldn't ever happen
            if (txtSaveLocation.Text.Equals("")) {
                MessageBox.Show("Please select the save location");
                return;
            }

            // Loop through each selected item in the listbox
            try
            {
                for (int counter = 0; counter < lstPlaylists.SelectedItems.Count; counter++)
                {
                    // Find the playlist object based on the selected playlist name
                    foreach (var playlist in allPlaylists.Items)
                    {
                        if (lstPlaylists.SelectedItems[counter].ToString().Equals(playlist.Name))
                        {
                            // Get playlist tracks
                            string playlistItemsURL = "";

                            switch (lstServerType.SelectedItem)
                            {
                                case ServerTypes.Jellyfin:
                                    playlistItemsURL = GenerateURL(ServerTypes.Jellyfin, URLTypes.JellyfinPlaylistItemsURL, playlist.Id);

                                    if (playlistItemsURL == "")
                                    {
                                        logger.LogError($"An error occurred while generating the Jellyfin playlist items URL");
                                        Log.CloseAndFlush();
                                        return;
                                    }

                                    break;
                                case ServerTypes.Emby:
                                    playlistItemsURL = GenerateURL(ServerTypes.Emby, URLTypes.EmbyPlaylistItemsURL, playlist.Id);

                                    if (playlistItemsURL == "")
                                    {
                                        logger.LogError($"An error occurred while generating the Emby playlist items URL");
                                        Log.CloseAndFlush();
                                        return;
                                    }
                                    break;
                            }

                            RestResult playlistItemsResult = await ExecuteRestRequest(playlistItemsURL);

                            if (playlistItemsResult.Status == "error")
                            {
                                logger.LogError($"The error {playlistItemsResult.ErrorMessage} occurred while reading the playlists items for the playlist {playlist.Name}");
                                Log.CloseAndFlush();
                                MessageBox.Show($"An error occurred while reading the playlists items for the playlist {playlist.Name}");
                                return;
                            }

                            // Parse JSON
                            Playlists currPlaylistTracks = JsonConvert.DeserializeObject<Playlists>(playlistItemsResult.Response.Content);

                            // Assign to the playlist object
                            playlist.PlaylistTracks = currPlaylistTracks;

                            // Write M3U
                            WriteM3U(playlist.Name, playlist.PlaylistTracks);
                        }
                    }
                }
            } catch(Exception ex)
            {
                logger.LogError($"The error {ex.Message} occurred exporting the playlists");
                Log.CloseAndFlush();
                MessageBox.Show($"An error occurred exporting the playlists.");

                return;
            }

            MessageBox.Show("All playlist(s) have been saved");
        }

        private async void BtnConnect_Click(object sender, EventArgs e) {
            if (demoMode)
            {
                txtURL.Background = new SolidColorBrush(Colors.Black);
                txtAPIKey.Background = new SolidColorBrush(Colors.Black);
                txtSaveLocation.Background = new SolidColorBrush(Colors.Black);
            }

            string embyServerKey = "SystemUpdateLevel";
            string jellyfinServerKey = "WebPath";

            // Validate required fields
            if (txtURL.Text.Equals("")) {
                MessageBox.Show("Please enter the URL of your Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals("")) {
                MessageBox.Show("Please enter your API key");
                return;
            }

            string serverType = ServerType();

            if (serverType == "") // This shouldn't ever happen. You shouldn't be able to click on this button until after selecting a server type
            {
                MessageBox.Show("Please select the server type");
                return;
            }
            else if (serverType == ServerTypes.Jellyfin)
            {
                // Validate that API Key is valid against Jellyfin
                string jellyfinPingURL = GenerateURL(ServerTypes.Jellyfin, URLTypes.JellyfinPingURL);
                
                if (jellyfinPingURL == "")
                {
                    logger.LogError($"An error occurred while generating Jellyfin ping URL");
                    Log.CloseAndFlush();
                    return;
                }

                RestResult jellyfinPingResult = await ExecuteRestRequest(jellyfinPingURL);

                if (jellyfinPingResult.Status == "error")
                {
                    logger.LogError($"An error occurred while connecting to {lstServerType.SelectedItem} with the error {jellyfinPingResult.ErrorMessage}");
                    Log.CloseAndFlush();
                    MessageBox.Show($"Unable to connect with {lstServerType.SelectedItem}. Please make sure that the URL and API key are correct");
                    return;
                }
                else if (jellyfinPingResult.Status == "ok") // Make sure that this is really a Jellyfin server
                {
                    JObject serverTypeResponse = JsonConvert.DeserializeObject<JObject>(jellyfinPingResult.Response.Content);

                    if (serverTypeResponse != null)
                    {
                        string checkKeyValue = serverTypeResponse[jellyfinServerKey]?.ToString();

                        if (checkKeyValue == null)
                        {
                            MessageBox.Show($"The server that you selected does not appear to be an {lstServerType.SelectedItem} server");
                            lstServerType.SelectedIndex = -1;
                            lstServerType.IsEnabled = true;
                            return;
                        }
                    }
                }

                if (lstUserAccounts.SelectedIndex == -1)
                {
                    //MessageBox.Show("Select the user account from the User dropdown");
                    RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.Second);
                    LoadUserAccounts();
                    return;
                }

                RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.Second);
            } else if (serverType == ServerTypes.Emby)
            {
                // Validate that API Key is valid against Emby
                string embySystemInfoURL = GenerateURL(ServerTypes.Emby, URLTypes.EmbyPingURL);

                if (embySystemInfoURL == "")
                {
                    logger.LogError($"An error occurred while generating Jellyfin ping URL");
                    Log.CloseAndFlush();
                    return;
                }

                RestResult checkEmbyResult = await ExecuteRestRequest(embySystemInfoURL);

                if (checkEmbyResult.Status == "error")
                {
                    logger.LogError($"An error occurred while connecting to {lstServerType.SelectedItem} with the error {checkEmbyResult.ErrorMessage}");
                    Log.CloseAndFlush();
                    MessageBox.Show($"Unable to connect with {lstServerType.SelectedItem}. Please make sure that the URL and API key are correct");
                    return;
                } else if (checkEmbyResult.Status == "ok") // Make sure that this is really an Emby server
                {
                    JObject serverTypeResponse = JsonConvert.DeserializeObject<JObject>(checkEmbyResult.Response.Content);

                    if (serverTypeResponse != null)
                    {
                        string checkKey = serverTypeResponse[embyServerKey]?.ToString();

                        if (checkKey == null)
                        {
                            MessageBox.Show($"The server that you selected does not appear to be an {lstServerType.SelectedItem} server");
                            lstServerType.SelectedIndex = -1;
                            lstServerType.IsEnabled = true;
                            return;
                        }
                    }
                }

                RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.Remaining);

                // Start a new thread to load all playlists so the UI doesn't lock up while its loading
                loadPlaylistsThread = new Thread(new ThreadStart(LoadPlaylists));
                loadPlaylistsThread.Start();
            }
        }

        private async void CheckForUpdates()
        {
            string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            string updateURL = "https://api.github.com/repos/SegiH/Emby-and-Jellyfin-Playlist-Exporter/releases/latest";

            RestResult updateResult = await ExecuteRestRequest(updateURL);

            if (updateResult.Status == "ok")
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(updateResult.Response.Content);

                string latestTag = jsonResponse["tag_name"]?.ToString();

                if (!string.IsNullOrWhiteSpace(currentVersion) && !String.IsNullOrWhiteSpace(latestTag) && IsVersionString(currentVersion) && IsVersionString(latestTag))
                {
                    Version currentVersionInt, latestTagInt;

                    try
                    {
                        // Convert both values to int for easy comparison
                        Version.TryParse(currentVersion, out currentVersionInt);

                        Version.TryParse(latestTag, out latestTagInt);

                        if (currentVersionInt < latestTagInt)
                        {
                            Assembly assembly = Assembly.GetExecutingAssembly();

                            string appName = System.IO.Path.GetFileNameWithoutExtension(assembly.Location);

                            string URL = "https://github.com/SegiH/Emby-and-Jellyfin-Playlist-Exporter/releases";

                            // Create the TextBlock
                            TextBlock textBlock = new TextBlock
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center
                            };

                            // Create the Run (static text)
                            Run run = new Run($"Update {latestTag} Available");

                            // Create the Hyperlink (clickable text)
                            Hyperlink hyperlink = new Hyperlink(run)
                            {
                                NavigateUri = new Uri(URL)
                            };

                            // Attach the event handler to the Hyperlink
                            hyperlink.RequestNavigate += Hyperlink_RequestNavigate;

                            // Add the Run (static text) to the TextBlock
                            textBlock.Inlines.Add(hyperlink);

                            // Add the TextBlock to the Window's content
                            MainGrid.Children.Add(textBlock);

                            textBlock.Margin = new System.Windows.Thickness(505, 0, 0, 0);
                        }
                    }
                    catch (FormatException)
                    {
                        logger.LogError($"Server type is not set in LoadPlaylists()");
                    }
                }
            }
        }

        private void ChkSelectAllNone_Checked(object sender, RoutedEventArgs e)
        {
            if (selectAllNone)
            {
                lstPlaylists.SelectedItems.Clear();
            }
            else
            {
                // Select all items.
                // There's a known issue where selecting all WPF items pragmatically doesn't show blue highlighting like when you manually select all items
                lstPlaylists.SelectedItems.Clear();

                // Loop through the list and select all items
                foreach (var item in lstPlaylists.Items)
                {
                    lstPlaylists.SelectedItems.Add(item);
                }
            }
        }

        private Task<RestResult> ExecuteRestRequest(string URL)
        {
            IRestClient client = null;

            try
            {
                // This prevents the SSL related error "The request was aborted: Could not create SSL/TLS secure channel."
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                client = new RestClient(URL);

                IRestResponse response = client.Execute(new RestRequest());

                if (response.StatusCode != HttpStatusCode.OK) {
                    throw new Exception();
                }

                RestResult result = new RestResult();

                result.Status = "ok";
                result.Response = response;

                return Task.FromResult(result);
            }
            catch (Exception err)
            {
                RestResult result = new RestResult();

                result.Status = "error";
                result.ErrorMessage = err.Message;

                return Task.FromResult(result);
            }
        }

        private string GenerateURL(string serverType, string urlType, string playlistId = "")
        {
            // Check if URL has trailing backslash
            string jellyfinPingURL = "<URL>/System/Info?api_key=<API_KEY>";
            string jellyfinPlaylistURL = "<URL>/Users/<USER_ID>/Items?format=json&Recursive=true&IncludeItemTypes=Playlist&api_key=<API_KEY>";
            string jellyfinPlaylistItemsURL = "<URL>/Playlists/<PLAYLIST_ID>/Items?Fields=Path&userId=<USER_ID>&api_key=<API_KEY>";
            string jellyfinUsersURL = "<URL>/Users?format=json&api_key=<API_KEY>";

            string embyPlaylistURL = "<URL>/emby/Items?format=json&Recursive=true&IncludeItemTypes=Playlist&api_key=<API_KEY>";
            string embyPlaylistItemsURL = "<URL>/Items?ParentId=<PLAYLIST_ID>&Format=json&api_key=<API_KEY>";
            string embyPingURL = "<URL>/emby/System/Info?api_key=<API_KEY>";

            string URL = txtURL.Text;

            if (URL.EndsWith("/"))
            {
                URL = URL.Substring(0, URL.Length - 1);
            }

            string newGeneratedURL;

            switch (serverType)
            {
                case ServerTypes.Jellyfin:
                    switch (urlType)
                    {
                        case URLTypes.JellyfinPingURL:
                            newGeneratedURL = jellyfinPingURL.Replace("<URL>", URL).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                        case URLTypes.JellyfinPlaylistURL:
                            newGeneratedURL = jellyfinPlaylistURL.Replace("<URL>", URL).Replace("<USER_ID>", allUserAccounts[lstUserAccounts.SelectedItem.ToString()]).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                        case URLTypes.JellyfinPlaylistItemsURL:
                            newGeneratedURL = jellyfinPlaylistItemsURL.Replace("<URL>", URL).Replace("<USER_ID>", allUserAccounts[lstUserAccounts.SelectedItem.ToString()]).Replace("<PLAYLIST_ID>", playlistId).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                        case URLTypes.JellyfinUsersURL:
                            newGeneratedURL = jellyfinUsersURL.Replace("<URL>", URL).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                    }
                    break;
                case ServerTypes.Emby:
                    switch (urlType)
                    {
                        case URLTypes.EmbyPlaylistURL:
                            newGeneratedURL = embyPlaylistURL.Replace("<URL>", URL).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                        case URLTypes.EmbyPlaylistItemsURL:
                            newGeneratedURL = embyPlaylistItemsURL.Replace("<URL>", URL).Replace("<PLAYLIST_ID>", playlistId).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                        case URLTypes.EmbyPingURL:
                            newGeneratedURL = embyPingURL.Replace("<URL>", URL).Replace("<API_KEY>", txtAPIKey.Password);
                            return newGeneratedURL;
                    }

                    break;
            }

            return "";
        }

        private void HideShowElements(string which, bool hidden)
        {
            List<string> element = new List<string>();

            if (which == Jellyfin_Playlist_Exporter.Models.HideShowElements.First)
            {
                element = firstElements;
            }
            else if (which == Jellyfin_Playlist_Exporter.Models.HideShowElements.Second)
            {
                element = secondElements;
            }
            else if (which == Jellyfin_Playlist_Exporter.Models.HideShowElements.Remaining)
            {
                element = remainingElements;
            }

            try
            {
                foreach (var item in element)
                {
                    if (item != null)
                    {
                        // Find the element using its name. If the element is found, hide it
                        if (FindName(item) is UIElement el)
                        {
                            el.Visibility = !hidden ? Visibility.Visible : Visibility.Hidden;
                        }
                    }
                }
            } catch(Exception ex) {
                logger.LogError($"An error occurred finding the item in HideShowElements() with the error {ex.Message}");
            }
        }

        // Event handler to open the URL when the hyperlink is clicked
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Open the default browser and navigate to the URL
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;  // Mark the event as handled
        }

        private void InitElementProperties()
        {
            /* 
              These elements have their coordinates saved so I can move them up or down 
              depending on if the user account dropdown is visible or not
            */
            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblServerType",
                Left = (int)lblServerType.Margin.Left,
                Top = (int)lblServerType.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lstServerType",
                Left = (int)lstServerType.Margin.Left,
                Top = (int)lstServerType.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblURL",
                Left = (int)lblURL.Margin.Left,
                Top = (int)lblURL.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtURL",
                Left = (int)txtURL.Margin.Left,
                Top = (int)txtURL.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblAPIKey",
                Left = (int)lblAPIKey.Margin.Left,
                Top = (int)lblAPIKey.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "btnConnect",
                Left = (int)btnConnect.Margin.Left,
                Top = (int)btnConnect.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtAPIKey",
                Left = (int)txtAPIKey.Margin.Left,
                Top = (int)txtAPIKey.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblUserAccount",
                Left = (int)lblUserAccount.Margin.Left,
                Top = (int)lblUserAccount.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lstUserAccounts",
                Left = (int)lstUserAccounts.Margin.Left,
                Top = (int)lstUserAccounts.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblSaveLocation",
                Left = (int)lblSaveLocation.Margin.Left,
                Top = (int)lblSaveLocation.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtSaveLocation",
                Left = (int)txtSaveLocation.Margin.Left,
                Top = (int)txtSaveLocation.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "btnSaveLocation",
                Left = (int)btnSaveLocation.Margin.Left,
                Top = (int)btnSaveLocation.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "chkSelectAllNone",
                Left = (int)chkSelectAllNone.Margin.Left,
                Top = (int)chkSelectAllNone.Margin.Top
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lstPlaylists",
                Left = (int)lstPlaylists.Margin.Left,
                Top = (int)lstPlaylists.Margin.Top
            });
        }

        private bool IsVersionString(string str)
        {
            return Version.TryParse(str, out _);
        }

        private void LoadPlaylists() {
            _ = Dispatcher.Invoke(async () =>
            {
                string playlistURL = "";

                string serverType = ServerType();

                if (serverType == ServerTypes.Jellyfin)
                {
                    playlistURL = GenerateURL(ServerTypes.Jellyfin, URLTypes.JellyfinPlaylistURL);

                    if (playlistURL == "")
                    {
                        logger.LogError($"An error occurred while generating the Jellyfin playlist URL");
                        Log.CloseAndFlush();
                        return;
                    }
                }
                else if (serverType == ServerTypes.Emby)
                {
                    playlistURL = GenerateURL(ServerTypes.Emby, URLTypes.EmbyPlaylistURL);

                    if (playlistURL == "")
                    {
                        logger.LogError($"An error occurred while generating the Emby playlist URL");
                        Log.CloseAndFlush();
                        return;
                    }
                }
                else // This shouldn't ever happen!
                {
                    logger.LogError($"Server type is not set in LoadPlaylists()");
                    Log.CloseAndFlush();
                    MessageBox.Show("Something went wrong. Please restart the app.");
                    return;
                }

                RestResult playlistResult = await ExecuteRestRequest(playlistURL);

                if (playlistResult.Status == "error")
                {
                    logger.LogError($"The error {playlistResult.ErrorMessage} occurred while reading the list of playlists");
                    Log.CloseAndFlush();
                    MessageBox.Show("An error occurred while reading the list of playlists");
                    return;
                }

                // Convert JSON payload to object of type Playlist
                allPlaylists = JsonConvert.DeserializeObject<Playlists>(playlistResult.Response.Content);

                if (demoMode)
                {
                    Playlists newArray = new Playlists();
                    Item[] items;

                    List<string> demoPlaylistPayload = new List<string>();

                    if (serverType == ServerTypes.Emby)
                    {
                        demoPlaylistPayload = embyDemoPlaylists;
                    }
                    else if (serverType == ServerTypes.Jellyfin)
                    {
                        demoPlaylistPayload = jellyfinDemoPlaylists;
                    }

                    items = new Item[demoPlaylistPayload.Count()];

                    int counter = 0;

                    foreach (var demoPlaylistName in demoPlaylistPayload)
                    {
                        Item newItem = new Item();
                        newItem.Name = demoPlaylistName;
                        items[counter] = newItem;

                        counter = counter + 1;
                    }

                    newArray.Items = items;
                    allPlaylists = newArray;
                }

                // Loop through each playlist
                foreach (var playlist in allPlaylists.Items)
                {
                    // Add playlist name to listbox
                    lstPlaylists.Items.Add(playlist.Name);
                }

                lstPlaylists.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("", System.ComponentModel.ListSortDirection.Ascending));

                RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.Remaining);
            });
        }

        // Load playlists
        private void LoadPlaylistValidation()
        {
            if (isLoading)
                return;

            // A user must be selected
            if (lstUserAccounts.SelectedIndex == -1 && ServerType() == ServerTypes.Jellyfin)
            {
                MessageBox.Show("Please select the user account");
                return;
            }

            // Server type must be selected
            if (lstServerType.SelectedIndex == -1)
            {
                MessageBox.Show("Please select the server type");
                return;
            }

            // Validate required fields
            if (txtURL.Text.Equals(""))
            {
                MessageBox.Show("Please enter the URL of your Emby/Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals(""))
            {
                MessageBox.Show("Please enter your API key");
                return;
            }

            // Jellyfin URL should always end in / since its a URL path
            if (!txtURL.Text.EndsWith("/"))
                txtURL.Text += "/";

            // Clear all items in case the user presses load playlists a 2nd time to refresh the list of playlists.
            lstPlaylists.Items.Clear();

            // Adjust elements as needed
            AdjustElementsTop();

            // Start a new thread to load all playlists so the UI doesn't lock up while its loading
            loadPlaylistsThread = new Thread(new ThreadStart(LoadPlaylists));
            loadPlaylistsThread.Start();
        }

        private void LoadUserAccounts() {
            Dispatcher.Invoke(async () =>
            {
                string usersURL = txtURL.Text + (!txtURL.Text.EndsWith("/") ? "/" : "") + "Users?format=json&api_key=" + txtAPIKey.Password;

                RestResult usersResult = await ExecuteRestRequest(usersURL);

                if (usersResult.Status == "error")
                {
                    logger.LogError($"The error {usersResult.ErrorMessage} occurred while getting the users");
                    Log.CloseAndFlush();
                    MessageBox.Show($"An error occurred while getting the users");
                    return;
                }

                // Clear all user accounts
                lstUserAccounts.Items.Clear();

                try
                {
                    // Convert JSON payload to object of type User Account
                    dynamic jsonResponse = JsonConvert.DeserializeObject(usersResult.Response.Content);

                    // Add empty option
                    lstUserAccounts.Items.Add("");

                    isAdding = true;

                    allUserAccounts.Clear();

                    for (int counter = 0; counter < jsonResponse.Count; counter++)
                    {
                        string name = jsonResponse[counter].Name.ToString().Replace("}", "").Replace("{", "");
                        string id = jsonResponse[counter].Id.ToString().Replace("}", "").Replace("{", "");

                        allUserAccounts.Add(name, id);
                        lstUserAccounts.Items.Add(name);
                    }

                    if (!Properties.Settings.Default.UserAccount.Equals(""))
                    {
                        lstUserAccounts.SelectedItem = Properties.Settings.Default.UserAccount;
                    }
                    else
                        lstUserAccounts.SelectedIndex = 0;

                    isAdding = false;
                }
                catch (Exception err)
                {
                    logger.LogError($"An error occurred while loaing the user accounts with the error \" ${err}");
                    Log.CloseAndFlush();
                    MessageBox.Show("An error occurred while loading the user accounts");
                    return;
                }
            });
        }

        // Event when an item is selected or deselected in the list box. The Export Playlists button is disabled if no items are selected in the listbox
        private void LstPlaylist_Selected(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItems.Count == 0) {
                btnExportPlaylists.Visibility = Visibility.Collapsed;
                btnExportPlaylists.Visibility = Visibility.Hidden;
            } else {
                btnExportPlaylists.Visibility = Visibility.Visible;
                btnExportPlaylists.Visibility = Visibility.Visible;
            }
        }

        private void LstServerType_SelectionChanged(object sender, RoutedEventArgs e) {
            if (isLoading)
            {
                return;
            }

            ResetLayout();

            RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.HideAll.ToString());

            switch (lstServerType.SelectedItem)
            {
                case ServerTypes.Emby:
                    lblUserAccount.Visibility = Visibility.Hidden;
                    lstUserAccounts.Visibility = Visibility.Hidden;
                    btnConnect.Visibility = Visibility.Hidden;

                    RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.First);

                    AdjustElementsTop();

                    break;
                case ServerTypes.Jellyfin:
                    lblUserAccount.Visibility = Visibility.Visible;
                    lstUserAccounts.Visibility = Visibility.Visible;
                    btnConnect.Visibility = Visibility.Visible;

                    RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.First);

                    break;
                default:
                    lstUserAccounts.SelectedIndex = -1;
                    RunStep(Jellyfin_Playlist_Exporter.Models.HideShowElements.HideAll);
                    break;
            }
        }
 
        private void LstUserAccounts_SelectionChanged(object sender, RoutedEventArgs e) {
            if (isAdding == true)
                return;

            // Since we add "" as the first item we have to make sure to ignore index 0
            // Clear all items if nothing is selected
            if (lstUserAccounts.SelectedIndex <= 0) {
                lstPlaylists.Items.Clear();
            }

            if (lstUserAccounts.SelectedIndex > 0)
            {
                if (demoMode)
                {
                    lstUserAccounts.Visibility = Visibility.Collapsed;
                    lstUserAccountsDemoMode.Visibility = Visibility.Visible;
                }

                LoadPlaylistValidation();
            }
        }

        private void ResetLayout()
        {
            foreach (var element in elementsProperties)
            {
                FrameworkElement el = FindName(element.ElementName) as FrameworkElement;

                if (el != null)
                {
                    var currentMargin = el.Margin;
                    el.Margin = new System.Windows.Thickness(el.Margin.Left, el.Margin.Top, currentMargin.Right, currentMargin.Bottom);
                }
            }
        }

        private void RunStep(string step)
        {
            string serverType = ServerType();

            switch (step)
            {
                case Jellyfin_Playlist_Exporter.Models.HideShowElements.HideAll:
                    HideShowElements("First", true);
                    HideShowElements("Second", true);
                    HideShowElements("Remaining", true);

                    Height = 100;

                    lstUserAccounts.SelectedIndex = -1;
                    chkSelectAllNone.IsChecked = false;

                    break;
                case Jellyfin_Playlist_Exporter.Models.HideShowElements.First:
                    HideShowElements("First", false);
                    HideShowElements("Second", true);
                    HideShowElements("Remaining", true);

                    Height = 170;

                    chkSelectAllNone.IsChecked = false;

                    break;
                case Jellyfin_Playlist_Exporter.Models.HideShowElements.Second:
                    HideShowElements("First", false);

                    if (serverType != "" && serverType == ServerTypes.Jellyfin)
                    {
                        HideShowElements("Second", false);
                    }

                    HideShowElements("Remaining", true);

                    Height = 225;

                    chkSelectAllNone.IsChecked = false;

                    break;
                case Jellyfin_Playlist_Exporter.Models.HideShowElements.Playlists:
                    HideShowElements("First", false);

                    if (serverType != "" && serverType == ServerTypes.Jellyfin)
                    {
                        HideShowElements("Second", false);
                    }

                    HideShowElements("Remaining", true);

                    Height = 225;

                    if (serverType == ServerTypes.Jellyfin)
                    {
                        Height = Height + topAdjustment;
                    }

                    break;
                case Jellyfin_Playlist_Exporter.Models.HideShowElements.Remaining:
                    HideShowElements("Remaining", false);

                    if (serverType != ServerTypes.Jellyfin)
                    {
                        Height = 650;
                    }
                    else
                    {
                        Height = 700;
                    }

                    break;
                default:
                    break;
            }
        }

        private void RemoveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure that you want to delete the saved settings ?", "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                txtURL.Text = "";
                txtAPIKey.Password = "";
                txtSaveLocation.Text = "";
                Properties.Settings.Default.Save();
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.URL = txtURL.Text;
            Properties.Settings.Default.APIKey = txtAPIKey.Password;

            if (lstUserAccounts.SelectedItem != null)
            {
                Properties.Settings.Default.UserAccount = lstUserAccounts.SelectedItem.ToString();
            }
            else
            {
                Properties.Settings.Default.UserAccount = "-1";
            }

            Properties.Settings.Default.SaveLocation = txtSaveLocation.Text;

            if (lstServerType.SelectedItem != null)
            {
                Properties.Settings.Default.ServerType = lstServerType.SelectedItem.ToString();
            }
            else
            {
                Properties.Settings.Default.ServerType = "";
            }
            Properties.Settings.Default.Save();

            MessageBox.Show("The settings have been saved");
        }

        private string ServerType()
        {
            switch (lstServerType.SelectedItem)
            {
                case ServerTypes.Emby:
                    return "Emby";
                case ServerTypes.Jellyfin:
                    return "Jellyfin";
                default:
                    return "";
            }
        }

        private void TxtURL_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (txtURL.Text.EndsWith("/"))
            {
                txtURL.Text = txtURL.Text.TrimEnd('/');
            }
        }

        // Write m3u file
        private void WriteM3U(string playlistName,Playlists playlistTracks) {
             string[] badChars = { "<", ">", ":", "\"", "/", "\\", "|", "?", "*" };

             foreach (var badChar in badChars)
             {
                playlistName = playlistName.Replace(badChar, "");
             }

            // Make sure that the save location has a trailing slash. Since VS works on Mac and Windows, we have to support delimiter for both platforms (and Linux if I can ever get it to work with Mono which doesn't currently support WPF)
            if (txtSaveLocation.Text.EndsWith("\\") == false && txtSaveLocation.Text.EndsWith("/") == false) {
                  txtSaveLocation.Text+= Path.DirectorySeparatorChar;
             }

            try
            {
                // Open m3u8 for writing
                string m3u8Path = txtSaveLocation.Text + playlistName + ".m3u8";

                using (StreamWriter sw = new StreamWriter(m3u8Path))
                {
                    // m3u8 header
                    sw.WriteLine("#EXTM3U");
                    sw.WriteLine("#Playlist name: " + playlistName);

                    // Loop through each track in the playlist
                    foreach (var track in playlistTracks.Items)
                    {
                        // Convert runtime ticks to seconds and round to nearest whole number
                        var duration = Math.Round((double)track.RunTimeTicks * 0.0000001, 0);

                        sw.WriteLine("#EXTINF:" + duration + ", " + (track.Artists.Length != 0 ? track.Artists[0] : "") + " - " + track.Name);
                        sw.WriteLine(track.Path);
                        sw.WriteLine("#{'type': 'track', 'id': '" + track.Id + "'}");
                    }
                }
            } catch (Exception ex)
            {
                logger.LogError($"\"An error occurred while writing the m3u8 playlist(s) with the error \" ${ex.Message}");
                Log.CloseAndFlush();
                MessageBox.Show("An error occurred while writing the m3u8 playlist(s)");
                return;
            }
        }
    }
}
