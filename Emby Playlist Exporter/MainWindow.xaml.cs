using Newtonsoft.Json;
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
using System.Xml.Linq;
using Jellyfin_Playlist_Exporter.Models;
using System.Linq;

/*
   TODO:

   Fix settings 
*/
namespace EmbyJellyfin_Playlist_Exporter
{
    public partial class MainWindow : Window {
        readonly List<string> firstElements = new List<string> { "lblURL", "txtURL", "lblAPIKey", "txtAPIKey", "btnConnect" };
        readonly List<string> secondElements = new List<string> { "lblUserAccount", "lstUserAccounts" };
        readonly List<string> remainingElements = new List<string> { "btnSaveLocation", "chkSelectAllNone", "lstPlaylists", "lblSaveLocation", "txtSaveLocation" };
        readonly List<ElementModel> elementsProperties = new List<ElementModel> { };

        Playlists allPlaylists;
        bool isAdding = false;
        bool isLoading = false;
        bool selectAllNone = false;
        readonly private Dictionary<string, string> allUserAccounts = new Dictionary<string, string>(); // Holds all user account data
        Thread loadPlaylistsThread;
        readonly ILogger<MainWindow> logger;
        int topAdjustment = 40;

        public MainWindow() {
            InitializeComponent();

            elementsProperties.Add(new ElementModel() {
                ElementName = "lblServerType",
                Left = 40,
                Top = 10
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lstServerType",
                Left = 124,
                Top = 13
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblURL",
                Left = 40,
                Top = 50
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtURL",
                Left = 124,
                Top = 53
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblAPIKey",
                Left = 40,
                Top = 90
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "btnConnect",
                Left = 532,
                Top = 93
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtAPIKey",
                Left = 124,
                Top = 93
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblUserAccount",
                Left = 40,
                Top = 130
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtUserAccount",
                Left = 124,
                Top = 133
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtPlaylists",
                Left = 124,
                Top = 173
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lblSaveLocation",
                Left = 40,
                Top = 210
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "txtSaveLocation",
                Left = 124,
                Top = 213
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "btnSaveLocation",
                Left = 532,
                Top = 213
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "chkSelectAllNone",
                Left = 40,
                Top = 250
            });

            elementsProperties.Add(new ElementModel()
            {
                ElementName = "lstPlaylists",
                Left = 40,
                Top = 285
            });

            Log.Logger = new LoggerConfiguration()
            .WriteTo.File("ejpe.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog();  // Add Serilog to the logger factory
            });

            logger = loggerFactory.CreateLogger<MainWindow>();

            RunStep(HideShowElementsEnum.HideAll.ToString());

            isLoading = true;

            // Load saved settings if they exist
            /*if (!Properties.Settings.Default.URL.Equals("")) {
                txtURL.Text = Properties.Settings.Default.URL;
                txtAPIKey.Password = Properties.Settings.Default.APIKey;
                txtSaveLocation.Text = Properties.Settings.Default.SaveLocation;

                lstServerType.SelectedItem = Properties.Settings.Default.ServerType;

                if (Properties.Settings.Default.ServerType == Servers.Emby)
                {
                    lblUserAccount.Visibility = Visibility.Hidden;
                    lstUserAccounts.Visibility = Visibility.Hidden;
                    btnConnect.Visibility = Visibility.Hidden;
                }

                if (!Properties.Settings.Default.UserAccount.Equals("") && !Properties.Settings.Default.UserAccount.Equals("-1")) {
                    LoadUserAccounts();

                    // Prevent lstUserAccounts from triggering BtnLoadPlaylists_Click when adding items 
                    this.isAdding = true;
                    lstUserAccounts.SelectedItem = Properties.Settings.Default.UserAccount;
                    this.isAdding = false;

                    BtnLoadPlaylists_Click(new object(), new EventArgs());
                }
            } else {
                // Use the desktop path as the default save location
                txtSaveLocation.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            lstSettings.Items.Add("Save settings");
            lstSettings.Items.Add("Remove saved settings");*/

            lstServerType.Items.Add("");
            lstServerType.Items.Add(ServerTypesEnum.Emby);
            lstServerType.Items.Add(ServerTypesEnum.Jellyfin);

            lstServerType.SelectedIndex = -1;

            isLoading = false;
        }
       
        private void AdjustElementsTop()
        {
            string serverType = ServerType();

            if (serverType != ServerTypesEnum.Jellyfin)
            {
                if (lblUserAccount.Visibility == Visibility.Hidden)
                {
                    var result = elementsProperties.FirstOrDefault(e => e.ElementName == "lblSaveLocation");

                    AdjustTop("btnLoadPlaylists", topAdjustment * -1);
                    AdjustTop("lblSaveLocation", topAdjustment * -1);
                    AdjustTop("txtSaveLocation", topAdjustment * -1);
                    AdjustTop("btnSaveLocation", topAdjustment * -1);
                    AdjustTop("chkSelectAllNone", topAdjustment * -1);
                    AdjustTop("lstPlaylists", topAdjustment * -1);
                }
            }
        }

        private void AdjustTop(string elementName, int topAdjustmentAmount)
        {
            var el = this.FindName(elementName) as FrameworkElement;

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
        private void BtnExportPlaylists_Click(object sender, EventArgs e) {
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
                            // Write M3U
                            WriteM3U(playlist.Name, playlist.PlaylistTracks);
                        }
                    }
                }
            } catch(Exception ex)
            {
                logger.LogError($"The error {ex.Message} occurred exporting the playlists");
                Log.CloseAndFlush();
                MessageBox.Show($"An error occurred exporting the playlists");
                return;
            }

            MessageBox.Show("All playlist(s) have been saved");
        }

        private void BtnConnect_Click(object sender, EventArgs e) {
            // Validate required fields
            if (txtURL.Text.Equals("")) {
                MessageBox.Show("Please enter the URL of your Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals("")) {
                MessageBox.Show("Please enter your API key");
                return;
            }

            if (lstUserAccounts.Items.Count == 0 && ServerType() == ServerTypesEnum.Jellyfin)
                LoadUserAccounts();

            string serverType = ServerType();

            if (serverType == "") // This shouldn't eveer happen. You shouldn't be able to click on this button until after selecting a server type
            {
                MessageBox.Show("Please select the server type");
                return;
            }
            else if (serverType == ServerTypesEnum.Jellyfin)
            {
                if (lstUserAccounts.SelectedIndex == -1)
                {
                    MessageBox.Show("Select the user account from the dropdown and click on load playlists");
                    return;
                }

                RunStep(HideShowElementsEnum.Second);
            } else if (serverType == ServerTypesEnum.Emby)
            {
                RunStep(HideShowElementsEnum.Playlists);
                RunStep(HideShowElementsEnum.Remaining);
                
                // Start a new thread to poad all playlists so the UI doesn't lock up while its loading
                this.loadPlaylistsThread = new Thread(new ThreadStart(this.LoadPlaylists));
                loadPlaylistsThread.Start();
            }
        }

        // Load playlists 
        private void BtnLoadPlaylists_Click(object sender, EventArgs e) {
            if (this.isLoading)
                return;

            // A user must be selected
            if (lstUserAccounts.SelectedIndex == -1 && ServerType() == ServerTypesEnum.Jellyfin) {
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
            if (txtURL.Text.Equals("")) {
                MessageBox.Show("Please enter the URL of your Emby/Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals("")) {
                MessageBox.Show("Please enter your API key");
                return;
            }

            // Jellyfin URL should always end in / since its a URL path
            if (!txtURL.Text.EndsWith("/"))
                txtURL.Text += "/";

            // Clear all items in case the user presses load playlists a 2nd time to refresh the list of playlists.
            lstPlaylists.Items.Clear();

            // Start a new thread to poad all playlists so the UI doesn't lock up while its loading
            this.loadPlaylistsThread = new Thread(new ThreadStart(this.LoadPlaylists));
            loadPlaylistsThread.Start();
        }

        private void ChkSelectAllNone_Checked(object sender, RoutedEventArgs e)
        {
            if (selectAllNone)
            {
                lstPlaylists.SelectedItems.Clear();
            }
            else
            {
                lstPlaylists.SelectAll();
            }
        }

        private void HideShowElements(string which, bool hidden)
        {
            List<string> element = new List<string>();

            if (which == HideShowElementsEnum.First)
            {
                element = firstElements;
            }
            else if (which == HideShowElementsEnum.Second)
            {
                element = secondElements;
            }
            else if (which == HideShowElementsEnum.Remaining)
            {
                element = remainingElements;
            }

            foreach (var item in element)
            {
                if (item != null)
                {
                    // Find the element using its name
                    var el = this.FindName(item) as UIElement;

                    // If the element is found, hide it
                    if (el != null)
                    {
                        el.Visibility = !hidden ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void LoadPlaylists() {
            this.Dispatcher.Invoke(() => {
                IRestClient client=null, plClient;
                IRestResponse response, plResponse;

                try {
                    // This prevents the SSL related error "The request was aborted: Could not create SSL/TLS secure channel."
                    ServicePointManager.Expect100Continue = true;
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                    this.Dispatcher.Invoke(() => {
                        // Call REST endpoint to get all playlists
                        string playlistURL = "";

                        string serverType = ServerType();

                        if (serverType == ServerTypesEnum.Jellyfin)
                        {
                            playlistURL = txtURL.Text + "/Users/" + this.allUserAccounts[lstUserAccounts.SelectedItem.ToString()] + "/Items?format=json&Recursive=true&IncludeItemTypes=Playlist&api_key=" + txtAPIKey.Password;
                        } else if (serverType == ServerTypesEnum.Emby)
                        {
                            playlistURL = txtURL.Text + "/emby/Items?format=json&Recursive=true&IncludeItemTypes=Playlist&api_key=" + txtAPIKey.Password;
                        } else
                        {
                            logger.LogError($"Server type is not set in LoadPlaylists()");
                            Log.CloseAndFlush();
                        }

                        client = new RestClient(playlistURL);
                    });

                    response = client.Execute(new RestRequest());

                    // Convert JSON payload to object of type Playlist
                    allPlaylists = JsonConvert.DeserializeObject<Playlists>(response.Content);

                    /*if (allPlaylists.TotalRecordCount > 0)
                    {
                        btnExportPlaylists.Visibility = Visibility.Visible;
                    } else
                    {
                        btnExportPlaylists.Visibility = Visibility.Hidden;
                    }*/
                } catch (Exception err) {
                    logger.LogError($"The error {err.Message} occurred while reading the list of playlists");
                    Log.CloseAndFlush();
                    MessageBox.Show("An error occurred while reading the list of playlists");
                    return;
                }

                // Loop through each playlist
                foreach (var playlist in allPlaylists.Items) {
                    try {

                        // Call REST endpoint to get all tracks in the current playlist
                        string playlistItemsURL = "";

                        switch (lstServerType.SelectedItem)
                        {
                            case ServerTypesEnum.Jellyfin:
                                playlistItemsURL = txtURL.Text + "/Playlists/" + playlist.Id + "/Items?Fields=Path&userId=" + this.allUserAccounts[lstUserAccounts.SelectedItem.ToString()] + "&api_key=" + txtAPIKey.Password;
                                break;
                            case ServerTypesEnum.Emby:
                                playlistItemsURL = txtURL.Text + "/Items?ParentId=" + playlist.Id + "&Format=json&api_key=" + txtAPIKey.Password;
                                break;
                        }

                        plClient = new RestClient(playlistItemsURL);
                        
                        plResponse = plClient.Execute(new RestRequest());

                        // Parse JSON
                        Playlists currPlaylistTracks = JsonConvert.DeserializeObject<Playlists>(plResponse.Content);

                        // Assign to the playlist object
                        playlist.PlaylistTracks = currPlaylistTracks;
                    } catch (Exception err) {
                        logger.LogError($"An error occurred while reading the playlist tracks from the playlist \" {playlist.Name} \" with the error \" ${err}");
                        Log.CloseAndFlush();
                        MessageBox.Show("An error occurred while reading the playlist tracks from the playlist");
                        return;
                    }

                    // Add playlist name to listbox
                    lstPlaylists.Items.Add(playlist.Name);
                }

                // Sort all items in the listbox
                lstPlaylists.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("", System.ComponentModel.ListSortDirection.Ascending));

                RunStep(HideShowElementsEnum.Remaining);
            });
        }

        private void LoadUserAccounts() {
            IRestClient client;
            IRestResponse response;

            // Clear all user accounts
            lstUserAccounts.Items.Clear();

            try {
                // This prevents the SSL related error "The request was aborted: Could not create SSL/TLS secure channel."
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                // Call REST endpoint to get user accounts
                string usersURL = txtURL.Text + (!txtURL.Text.EndsWith("/") ? "/" : "") + "Users?format=json&api_key=" + txtAPIKey.Password;
                client = new RestClient(usersURL);

                response = client.Execute(new RestRequest());

                // Convert JSON payload to object of type User Account
                dynamic jsonResponse = JsonConvert.DeserializeObject(response.Content);

                // Add empty option
                lstUserAccounts.Items.Add("");

                this.isAdding = true;

                for (int counter = 0; counter < jsonResponse.Count; counter++) {
                    string name = jsonResponse[counter].Name.ToString().Replace("}", "").Replace("{", "");
                    string id = jsonResponse[counter].Id.ToString().Replace("}", "").Replace("{", "");

                    allUserAccounts.Add(name, id);
                    lstUserAccounts.Items.Add(name);
                }


                if (!Properties.Settings.Default.UserAccount.Equals("")) {
                    lstUserAccounts.SelectedItem = Properties.Settings.Default.UserAccount;
                } else
                    lstUserAccounts.SelectedIndex = 0;

                this.isAdding = false;
            } catch (Exception err) {
                logger.LogError($"\"An error occurred while reading the user accounts with the error \" ${err}");
                Log.CloseAndFlush();
                MessageBox.Show("An error occurred while reading the user account");
                return;
            }
        }

        // Event when an item is selected or deselected in the list box. The Export Playlists button is disabled if no items are selected in the listbox
        private void LstPlaylist_Selected(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItems.Count == 0) {
                btnExportPlaylists.IsEnabled = false;
                btnExportPlaylists.Visibility = Visibility.Hidden;
            } else {
                btnExportPlaylists.IsEnabled = true;
                btnExportPlaylists.Visibility = Visibility.Visible;
            }
        }

        // Settings dropdown changed
        private void LstSettings_SelectionChanged(object sender, RoutedEventArgs e) {
            switch (lstSettings.SelectedIndex) {
                case 0: // Save Settings
                    if (txtURL.Text.Equals("")) {
                        MessageBox.Show("Please enter the URL of your Jellyfin instance");
                    }

                    // A user must be selected
                    if (ServerType() == ServerTypesEnum.Jellyfin && lstUserAccounts.SelectedIndex == -1) {
                        MessageBox.Show("Please select the user account");
                        return;
                    }

                    if (txtAPIKey.Password.Equals("")) {
                        MessageBox.Show("Please enter your API key");
                        return;
                    }

                    // Jellyfin URL should always end in / since its a URL path
                    if (txtURL.Text.EndsWith("/") == false)
                        txtURL.Text += "/";

                    // If not specified, clear 
                    if (txtSaveLocation.Text.Equals("")) {
                        txtSaveLocation.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    }

                    Properties.Settings.Default.URL = txtURL.Text;
                    Properties.Settings.Default.APIKey = txtAPIKey.Password;

                    if (lstUserAccounts.SelectedItem != null)
                    {
                        Properties.Settings.Default.UserAccount = lstUserAccounts.SelectedItem.ToString();
                    } else
                    {
                        Properties.Settings.Default.UserAccount = "-1";
                    }

                    Properties.Settings.Default.SaveLocation = txtSaveLocation.Text;

                    if (lstServerType.SelectedItem != null)
                    {
                        Properties.Settings.Default.ServerType = lstServerType.SelectedItem.ToString();
                    } else
                    {
                        Properties.Settings.Default.ServerType = "";
                    }
                    Properties.Settings.Default.Save();

                    MessageBox.Show("The settings have been saved");

                    lstSettings.SelectedIndex = -1;

                    break;
                case 1: // Remove saved settings
                    if (MessageBox.Show("Are you sure that you want to delete the saved settings ?","Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                        txtURL.Text = "";
                        txtAPIKey.Password = "";
                        txtSaveLocation.Text = "";
                        Properties.Settings.Default.Save();
                    }

                    lstSettings.SelectedIndex = -1;

                    break;
            }
        }

        private void LstServerType_SelectionChanged(object sender, RoutedEventArgs e) {
            if (isLoading)
            {
                return;
            }

            switch (lstServerType.SelectedItem)
            {
                case ServerTypesEnum.Emby:
                    lstServerType.IsEnabled = false;

                    lblUserAccount.Visibility = Visibility.Hidden;
                    lstUserAccounts.Visibility = Visibility.Hidden;
                    btnConnect.Visibility = Visibility.Hidden;

                    AdjustElementsTop();

                    RunStep(HideShowElementsEnum.First);

                    //chkSelectAllNone.Margin = new System.Windows.Thickness(40, chkSelectAllNone.Margin.Top, chkSelectAllNone.Margin.Right, chkSelectAllNone.Margin.Bottom);

                    break;
                case ServerTypesEnum.Jellyfin:
                    lstServerType.IsEnabled = false;

                    lblUserAccount.Visibility = Visibility.Visible;
                    lstUserAccounts.Visibility = Visibility.Visible;
                    btnConnect.Visibility = Visibility.Visible;

                    RunStep(HideShowElementsEnum.First);

                    break;
                default:
                    lstUserAccounts.SelectedIndex = -1;
                    RunStep(HideShowElementsEnum.HideAll);
                    break;
            }
        }
        private void LstUserAccounts_SelectionChanged(object sender, RoutedEventArgs e) {
            if (this.isAdding == true)
                return;

            // Since we add "" as the first item we have to make sure to ignore index 0
            // Clear all items if nothing is selected
            if (lstUserAccounts.SelectedIndex <= 0) {
                lstPlaylists.Items.Clear();
            }

            //ResetLayout();
            RunStep(HideShowElementsEnum.Playlists);

            if (lstUserAccounts.SelectedIndex > 0)
            {
                BtnLoadPlaylists_Click(new object(), new EventArgs());
            }
        }

        private void RunStep(string step)
        {
            string serverType = ServerType();

            switch (step)
            {
                case HideShowElementsEnum.HideAll:
                    HideShowElements("First", true);
                    HideShowElements("Second", true);
                    HideShowElements("Remaining", true);

                    this.Height = 100;

                    lstUserAccounts.SelectedIndex = -1;

                    break;
                case HideShowElementsEnum.First:
                    HideShowElements("First", false);
                    HideShowElements("Second", true);
                    //HideShowElements("Playlists", true);
                    HideShowElements("Remaining", true);
                    this.Height = 180;
                    break;
                case HideShowElementsEnum.Second:
                    HideShowElements("First", false);

                    if (serverType != "" && serverType == ServerTypesEnum.Jellyfin)
                    {
                        HideShowElements("Second", false);
                    }

                    //HideShowElements("Playlists", true);
                    HideShowElements("Remaining", true);
                    this.Height = 225;
                    break;
                case HideShowElementsEnum.Playlists:
                    HideShowElements("First", false);

                    if (serverType != "" && serverType == ServerTypesEnum.Jellyfin)
                    {
                        HideShowElements("Second", false);
                    }

                    HideShowElements("Remaining", true);

                    this.Height = 225;

                    if (serverType == ServerTypesEnum.Jellyfin)
                    {
                        this.Height = this.Height + topAdjustment;
                    }
                    break;
                case HideShowElementsEnum.Remaining:
                    HideShowElements("Remaining", false);

                    if (serverType != ServerTypesEnum.Jellyfin)
                    {
                        this.Height = 650;
                    }
                    else
                    {
                        this.Height = 700;
                    }

                    break;
                default:
                    break;
            }
        }

        private string ServerType()
        {
            switch (lstServerType.SelectedItem)
            {
                case ServerTypesEnum.Emby:
                    return "Emby";
                case ServerTypesEnum.Jellyfin:
                    return "Jellyfin";
                default:
                    return "";
            }
        }

        private void txtURL_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (txtURL.Text.EndsWith("/"))
            {
                txtURL.Text = txtURL.Text.TrimEnd('/');
            }
        }

        // Write m3u file
        private void WriteM3U(string playlistName,Playlists playlistTracks) {
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
