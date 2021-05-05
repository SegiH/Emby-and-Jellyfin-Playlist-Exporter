using Newtonsoft.Json;
using Ookii.Dialogs.Wpf;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;

namespace Jellyfin_Playlist_Exporter {
    public partial class MainWindow : Window {
        bool isAdding = false;
        Playlists allPlaylists;
        readonly private Dictionary<string, string> allUserAccounts = new Dictionary<string, string>(); // Holds all projects
        Thread loadPlaylistsThread;

        public MainWindow() {
            InitializeComponent();

            // Load saved settings if they exist
            if (!Properties.Settings.Default.JellyfinURL.Equals("")) {
                txtJellyfinURL.Text = Properties.Settings.Default.JellyfinURL;
                txtAPIKey.Password = Properties.Settings.Default.APIKey;
                txtSaveLocation.Text = Properties.Settings.Default.SaveLocation;

                if (!Properties.Settings.Default.UserAccount.Equals("")) {
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
            lstSettings.Items.Add("Remove saved settings");
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
            for (int counter = 0; counter < lstPlaylists.SelectedItems.Count; counter++) {
                // Find the playlist object based on the selected playlist name
                foreach (var playlist in allPlaylists.Items) {
                    if (lstPlaylists.SelectedItems[counter].ToString().Equals(playlist.Name)) {
                        // Write M3U
                        WriteM3U(playlist.Name, playlist.PlaylistTracks);
                    }
                }
            }

            MessageBox.Show("All playlist(s) have been saved");
        }

        private void BtnLoadUserAccounts_Click(object sender, EventArgs e) {
            // Validate required fields
            if (txtJellyfinURL.Text.Equals("")) {
                MessageBox.Show("Please enter the URL of your Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals("")) {
                MessageBox.Show("Please enter your API key");
                return;
            }

            if (lstUserAccounts.Items.Count == 0)
                LoadUserAccounts();

            if (lstUserAccounts.SelectedIndex == -1 || lstUserAccounts.SelectedIndex == 0)
                MessageBox.Show("Select the user account from the dropdown and click on load playlists");
            else
                BtnLoadPlaylists_Click(new object(), new EventArgs());
        }

        // Load playlists 
        private void BtnLoadPlaylists_Click(object sender, EventArgs e) {
            // A user must be selected
            if (lstUserAccounts.SelectedIndex == -1 || lstUserAccounts.SelectedIndex == 0) {
                MessageBox.Show("Please select the user account");
                return;
            }

            // Validate required fields
            if (txtJellyfinURL.Text.Equals("")) {
                MessageBox.Show("Please enter the URL of your Jellyfin instance");
                return;
            }

            if (txtAPIKey.Password.Equals("")) {
                MessageBox.Show("Please enter your API key");
                return;
            }

            // Jellyfin URL should always end in / since its a URL path
            if (!txtJellyfinURL.Text.EndsWith("/"))
                txtJellyfinURL.Text += "/";

            // Clear all items in case the user presses load playlists a 2nd time to refresh the list of playlists.
            lstPlaylists.Items.Clear();

            // Start a new thread to poad all playlists so the UI doesn't lock up while its loading
            this.loadPlaylistsThread = new Thread(new ThreadStart(this.LoadPlaylists));
            loadPlaylistsThread.Start();
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
                        client = new RestClient(txtJellyfinURL.Text + "Users/" + this.allUserAccounts[lstUserAccounts.SelectedItem.ToString()] + "/Items?format=json&Recursive=true&IncludeItemTypes=Playlist&api_key=" + txtAPIKey.Password);
                    });

                    response = client.Execute(new RestRequest());

                    // Convert JSON payload to object of type Playlist
                    allPlaylists = JsonConvert.DeserializeObject<Playlists>(response.Content);
                } catch (Exception err) {
                    MessageBox.Show("An error occurred while reading the list of playlists with the error " + err);
                    return;
                }

                // Loop through each playlist
                foreach (var playlist in allPlaylists.Items) {
                    try {

                        // Call REST endpoint to get all tracks in the current playlist
                        plClient = new RestClient(txtJellyfinURL.Text + "Playlists/" + playlist.Id + "/Items?Fields=Path&userId=" + this.allUserAccounts[lstUserAccounts.SelectedItem.ToString()] + "&api_key=" + txtAPIKey.Password);
                        plResponse = plClient.Execute(new RestRequest());

                        // Parse JSON
                        Playlists currPlaylistTracks = JsonConvert.DeserializeObject<Playlists>(plResponse.Content);

                        // Assign to the playlist object
                        playlist.PlaylistTracks = currPlaylistTracks;
                    } catch (Exception err) {
                        MessageBox.Show("An error occurred while reading the playlist tracks from the playlist " + playlist.Name + " with the error " + err);
                        return;
                    }

                    // Add playlist name to listbox
                    lstPlaylists.Items.Add(playlist.Name);
                }

                // Sort all items in the listbox
                lstPlaylists.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("", System.ComponentModel.ListSortDirection.Ascending));
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

                // Call REST endpoint to get all playlists - 
                client = new RestClient(txtJellyfinURL.Text + (!txtJellyfinURL.Text.EndsWith("/") ? "/" : "") + "Users?format=json&api_key=" + txtAPIKey.Password);

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
                MessageBox.Show("An error occurred while reading the list of playlists with the error " + err);
                return;
            }
        }

        // Event when an item is selected or deselected in the list box. The Export Playlists button is disabled if no items are selected in the listbox
        private void LstPlaylist_Selected(object sender, RoutedEventArgs e) {
            if (lstPlaylists.SelectedItems.Count == 0) {
                btnExportPlaylists.IsEnabled = false;
            } else {
                btnExportPlaylists.IsEnabled = true;
            }
        }

        // Settings dropdown changed
        private void LstSettings_SelectionChanged(object sender, RoutedEventArgs e) {
            switch (lstSettings.SelectedIndex) {
                case 0: // Save Settings
                    if (txtJellyfinURL.Text.Equals("")) {
                        MessageBox.Show("Please enter the URL of your Jellyfin instance");
                    }

                    // A user must be selected
                    if (lstUserAccounts.SelectedIndex == -1 || lstUserAccounts.SelectedIndex == 0) {
                        MessageBox.Show("Please select the user account");
                        return;
                    }

                    if (txtAPIKey.Password.Equals("")) {
                        MessageBox.Show("Please enter your API key");
                        return;
                    }

                    // Jellyfin URL should always end in / since its a URL path
                    if (txtJellyfinURL.Text.EndsWith("/") == false)
                        txtJellyfinURL.Text += "/";

                    // If not specified, clear 
                    if (txtSaveLocation.Text.Equals("")) {
                        txtSaveLocation.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    }

                    Properties.Settings.Default.JellyfinURL = txtJellyfinURL.Text;
                    Properties.Settings.Default.APIKey = txtAPIKey.Password;
                    Properties.Settings.Default.UserAccount = lstUserAccounts.SelectedItem.ToString();
                    Properties.Settings.Default.SaveLocation = txtSaveLocation.Text;
                    Properties.Settings.Default.Save();

                    MessageBox.Show("The settings have been saved");

                    lstSettings.SelectedIndex = -1;

                    break;
                case 1: // Remove saved settings
                    if (MessageBox.Show("Are you sure that you want to delete the saved settings ?","Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                        txtJellyfinURL.Text = "";
                        txtAPIKey.Password = "";
                        txtSaveLocation.Text = "";
                        Properties.Settings.Default.Save();
                    }

                    lstSettings.SelectedIndex = -1;

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
        }

        // Write M3u
        private void WriteM3U(string playlistName,Playlists playlistTracks) {
             // Make sure that the save location has a trailing slash. Since VS works on Mac and Windows, we have to support delimiter for both platforms (and Linux if I can ever get it to work with Mono which doesn't currently support WPF)
             if (txtSaveLocation.Text.EndsWith("\\") == false && txtSaveLocation.Text.EndsWith("/") == false) {
                  txtSaveLocation.Text+= Path.DirectorySeparatorChar;
             }
 
             // Open m3u8 for writing
             using (StreamWriter sw = new StreamWriter(txtSaveLocation.Text + playlistName + ".m3u8")) {
                  // m3u8 header
                  sw.WriteLine("#EXTM3U");
                  sw.WriteLine("#Playlist name: " + playlistName);

                  // Loop through each track in the playlist
                  foreach (var track in playlistTracks.Items) {
                       // Convert runtime ticks to seconds and round to nearest whole number
                       var duration = Math.Round((double)track.RunTimeTicks * 0.0000001, 0);

                       sw.WriteLine("#EXTINF:" + duration + ", " + (track.Artists.Length != 0 ? track.Artists[0] : "") + " - " + track.Name);
                       sw.WriteLine(track.Path);
                       sw.WriteLine("#{'type': 'track', 'id': '" + track.Id + "'}");
                  }
             }
        }
    }
}
