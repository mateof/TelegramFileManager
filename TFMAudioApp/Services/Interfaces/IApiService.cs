using Refit;
using TFMAudioApp.Models;

namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Refit interface for TFM Mobile API
/// All endpoints return ApiResult wrapper
/// </summary>
public interface IApiService
{
    #region Channels

    [Get("/api/mobile/channels")]
    Task<ApiResult<List<Channel>>> GetAllChannelsAsync();

    [Get("/api/mobile/channels/favorites")]
    Task<ApiResult<List<Channel>>> GetFavoriteChannelsAsync();

    [Get("/api/mobile/channels/folders")]
    Task<ApiResult<ChannelsWithFolders>> GetChannelsWithFoldersAsync();

    [Get("/api/mobile/channels/{id}/info")]
    Task<ApiResult<ChannelDetail>> GetChannelInfoAsync(long id);

    [Get("/api/mobile/channels/{id}/files")]
    Task<ApiResult<List<ChannelFile>>> GetChannelFilesAsync(long id, [Query] ChannelFilesRequest request);

    [Get("/api/mobile/channels/{id}/browse")]
    Task<ApiResult<FolderContents>> BrowseChannelAsync(long id, [Query] BrowseRequest request);

    [Post("/api/mobile/channels/{id}/favorite")]
    Task<ApiResult<object>> AddToFavoritesAsync(long id);

    [Delete("/api/mobile/channels/{id}/favorite")]
    Task RemoveFromFavoritesAsync(long id);

    #endregion

    #region Playlists

    [Get("/api/mobile/playlists")]
    Task<ApiResult<List<Playlist>>> GetAllPlaylistsAsync();

    [Get("/api/mobile/playlists/{id}")]
    Task<ApiResult<PlaylistDetail>> GetPlaylistAsync(string id);

    [Post("/api/mobile/playlists")]
    Task<ApiResult<Playlist>> CreatePlaylistAsync([Body] CreatePlaylistRequest request);

    [Put("/api/mobile/playlists/{id}")]
    Task<ApiResult<Playlist>> UpdatePlaylistAsync(string id, [Body] UpdatePlaylistRequest request);

    [Delete("/api/mobile/playlists/{id}")]
    Task DeletePlaylistAsync(string id);

    [Post("/api/mobile/playlists/{id}/tracks")]
    Task<ApiResult<Track>> AddTrackToPlaylistAsync(string id, [Body] AddTrackRequest request);

    [Delete("/api/mobile/playlists/{id}/tracks/{fileId}")]
    Task RemoveTrackFromPlaylistAsync(string id, string fileId);

    [Put("/api/mobile/playlists/{id}/tracks/reorder")]
    Task<ApiResult<PlaylistDetail>> ReorderTracksAsync(string id, [Body] ReorderTracksRequest request);

    #endregion

    #region Files

    [Get("/api/mobile/files/telegram/{channelId}")]
    Task<ApiResult<FolderContents>> BrowseTelegramFilesAsync(string channelId, [Query] BrowseRequest request);

    [Get("/api/mobile/files/local")]
    Task<ApiResult<FolderContents>> BrowseLocalFilesAsync([Query] BrowseRequest request);

    [Get("/api/mobile/files/telegram/{channelId}/{fileId}")]
    Task<ApiResult<ChannelFile>> GetFileInfoAsync(string channelId, string fileId);

    #endregion

    #region Streaming

    [Get("/api/mobile/stream/info/{channelId}/{fileId}")]
    Task<ApiResult<AudioInfo>> GetAudioInfoAsync(string channelId, string fileId);

    [Post("/api/mobile/stream/preload/{channelId}/{fileId}")]
    Task<ApiResult<object>> PreloadAudioAsync(string channelId, string fileId);

    #endregion
}
