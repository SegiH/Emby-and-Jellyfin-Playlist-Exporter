# Emby and Jellyfin PLaylist Exporter

Emby and Jellyfin Playlist Exporter is a Windows application developed in C# which lets you export your playlists from Emby or Jellyfin in m3u8 format which is a playlist format that can be easily backed up and restored 
and can also work with modern media players like VLC.

# Building the application
I have included Windows 64-bit binaries but if you want to build the application yourself, download Visual Studio 2019 and choose build solution

## Requirements
 - A media server running Emby or Jellyfin
 - An API key from Emby/Jellyfin. API keys can be created by going to the dashboard and choosing API Keys

## Usage
1. Run the app
 1. Enter the URL of Jellyfin/Emby without anything after the domain. For example: http://www.myembydomain.com:8096
 1. Enter the API key. 
 1. Click On Load User Accounts
 1. Select a user account from the dropdown. All of the playlists should load
 1. Select a location where the playlists will be saved
 1. Select the playlist that you want to export. If you want to select more than one playlist, hold Ctrl while clicking on the playlist name.
 1. Click on Export to export the selected playlists which will be saved into the location you selected in step 5.

If you want to save the URL, API key, User Account and Save Location, you can choose Save Settings from the settings dropdown. These settings are only saved on the local computer. When you load the app after saving the settings, all of the saved settings will be filled in automatically and the playlists will be loaded automatically.

Note: When you select a user account from the dropdown, you might notice that no matter which user account you choose, the same playlists are shown. This is because the playlists are not specific to the user but are part of a library.
There is currently a bug in Jellyfin where you can only get the playlist items by referencing a Jellyfin user. If you are using Emby, you can get all of the playlists without selecting a user account first. In order to make this application work with both Emby and Jellyfin, I added a dropdown to choose the user account. Once this is fixed in Jellyfin, I will update the app and remove the user accounts dropdown.