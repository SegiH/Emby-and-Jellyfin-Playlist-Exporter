# Emby and Jellyfin Playlist Exporter

Emby and Jellyfin Playlist Exporter is a Windows application which lets you export your playlists from Emby or Jellyfin in m3u8 format which is a playlist format that can be easily backed up and restored and can also work with modern media players like VLC.

# Building the application
I have included Windows 64-bit binaries but if you want to build the application yourself, download Visual Studio 2019 and build the solution

## Requirements
 - A media server running Emby or Jellyfin
 - An API key from Emby/Jellyfin. API keys can be created by going to the dashboard and choosing API Keys

## Usage
1. Run the app
 1. Enter the URL of Jellyfin/Emby without anything after the domain. For example: http://www.myembydomain.com:8096
 1. Enter the API key. 
 1. If you use Jellyfin, click On Load User Accounts and select the user.
 1. Select a location where the playlists will be saved
 1. Select the playlist that you want to export. If you want to select more than one playlist, hold Ctrl while clicking on the playlist name.
 1. Click on Export to export the selected playlists which will be saved into the location you selected in step 5.