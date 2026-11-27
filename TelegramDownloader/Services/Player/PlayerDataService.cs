using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Player
{
    /// <summary>A request the player data endpoints refuse with <c>400 invalid_request</c>.</summary>
    public sealed class PlayerDataValidationException : Exception
    {
        public PlayerDataValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Shared player data of the single user of the server: favourite tracks,
    /// play history and resume positions, so every device (web player, audio
    /// app) sees the same. Every write raises <see cref="Changed"/>, which the
    /// events bridge sends as <c>PlayerDataChanged</c> on <c>/hubs/events</c>.
    /// </summary>
    public class PlayerDataService
    {
        private readonly IPlayerDataStore _store;
        private readonly Func<DateTime> _clock;

        /// <summary>Raised after every successful write.</summary>
        public event Action<PlayerDataChangedEventDto>? Changed;

        public PlayerDataService(IPlayerDataStore store) : this(store, () => DateTime.UtcNow) { }

        public PlayerDataService(IPlayerDataStore store, Func<DateTime> clock)
        {
            _store = store;
            _clock = clock;
        }

        #region Favourites

        public async Task<ApiPlayerFavoritesPageDto> ListFavorites(int page, int pageSize)
        {
            if (page < 1) throw new PlayerDataValidationException("page must be 1 or more");
            RequireLimit(pageSize, "pageSize");
            var (items, total) = await _store.ListFavorites((page - 1) * pageSize, pageSize);
            return new ApiPlayerFavoritesPageDto
            {
                Items = items.Select(f => ApiPlayerFavoriteDto.From(f.Track, f.AddedAt)).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public Task<List<string>> FavoriteKeys() => _store.FavoriteKeys();

        public async Task<ApiPlayerFavoriteDto> AddFavorite(ApiPlayerTrackDto? track)
        {
            var normalized = RequireTrack(track);
            var stored = await _store.UpsertFavorite(normalized, _clock());
            Raise(PlayerDataKinds.Favorites, normalized.Key);
            return ApiPlayerFavoriteDto.From(stored.Track ?? normalized, stored.AddedAt);
        }

        public async Task RemoveFavorite(string? key)
        {
            var normalized = RequireKey(key);
            await _store.DeleteFavorite(normalized);
            Raise(PlayerDataKinds.Favorites, normalized);
        }

        #endregion

        #region History

        public async Task<ApiPlayerHistoryEntryDto> AddHistory(ApiPlayerHistoryWriteRequest? request)
        {
            if (request == null) throw new PlayerDataValidationException("A body is required");
            var track = RequireTrack(request.Track);
            if (request.PositionSeconds is < 0) throw new PlayerDataValidationException("positionSeconds cannot be negative");

            var entry = new PlayerHistoryDoc
            {
                Key = track.Key!,
                Track = track,
                PlayedAt = request.PlayedAt?.UtcDateTime ?? _clock(),
                PositionSeconds = request.PositionSeconds,
                Completed = request.Completed ?? false
            };
            await _store.InsertHistory(entry);

            var excess = PlayerDataRules.HistoryExcess(await _store.CountHistory());
            if (excess > 0) await _store.DeleteOldestHistory(excess);

            Raise(PlayerDataKinds.History, track.Key);
            return ToDto(entry);
        }

        public async Task<ApiPlayerHistoryPageDto> ListHistory(int limit, DateTime? before)
        {
            RequireLimit(limit, "limit");
            var rows = await _store.ListHistory(before, limit + 1);
            return new ApiPlayerHistoryPageDto
            {
                Items = rows.Take(limit).Select(ToDto).ToList(),
                HasMore = rows.Count > limit
            };
        }

        public async Task<List<ApiPlayerTopTrackDto>> TopPlayed(int limit, int? days)
        {
            RequireLimit(limit, "limit");
            if (days is < 1) throw new PlayerDataValidationException("days must be 1 or more");
            DateTime? since = days.HasValue ? _clock().AddDays(-days.Value) : null;
            var rows = await _store.HistorySince(since);
            return PlayerDataRules.TopPlayed(rows.Select(r => (r.Track, r.PlayedAt)), limit);
        }

        public async Task ClearHistory()
        {
            await _store.ClearHistory();
            Raise(PlayerDataKinds.History, null);
        }

        private static ApiPlayerHistoryEntryDto ToDto(PlayerHistoryDoc d) => new()
        {
            Id = d.Id.ToString(),
            Track = d.Track,
            PlayedAt = d.PlayedAt,
            PositionSeconds = d.PositionSeconds,
            Completed = d.Completed
        };

        #endregion

        #region Resume

        /// <summary>
        /// Saves a resume position, or deletes it when the position means the
        /// track was finished (<see cref="PlayerDataRules.IsFinished"/>).
        /// </summary>
        public async Task<ApiPlayerResumeDto> SaveResume(ApiPlayerResumeWriteRequest? request)
        {
            if (request == null) throw new PlayerDataValidationException("A body is required");
            var track = RequireTrack(request.Track);
            if (request.PositionSeconds < 0) throw new PlayerDataValidationException("positionSeconds cannot be negative");
            if (request.DurationSeconds < 0) throw new PlayerDataValidationException("durationSeconds cannot be negative");

            var entry = new PlayerResumeDoc
            {
                Key = track.Key!,
                Track = track,
                PositionSeconds = request.PositionSeconds,
                DurationSeconds = request.DurationSeconds,
                UpdatedAt = _clock()
            };

            var finished = PlayerDataRules.IsFinished(request.PositionSeconds, request.DurationSeconds);
            if (finished)
                await _store.DeleteResume(track.Key);
            else
                await _store.UpsertResume(entry);

            Raise(PlayerDataKinds.Resume, track.Key);
            var dto = ToDto(entry);
            dto.Finished = finished;
            return dto;
        }

        public async Task<ApiPlayerResumeDto?> GetResume(string? key)
        {
            var doc = await _store.GetResume(RequireKey(key));
            return doc == null ? null : ToDto(doc);
        }

        public async Task<List<ApiPlayerResumeDto>> ListResume(int limit)
        {
            RequireLimit(limit, "limit");
            return (await _store.ListResume(limit)).Select(ToDto).ToList();
        }

        public async Task RemoveResume(string? key)
        {
            var normalized = RequireKey(key);
            await _store.DeleteResume(normalized);
            Raise(PlayerDataKinds.Resume, normalized);
        }

        private static ApiPlayerResumeDto ToDto(PlayerResumeDoc d) => new()
        {
            Track = d.Track,
            PositionSeconds = d.PositionSeconds,
            DurationSeconds = d.DurationSeconds,
            UpdatedAt = d.UpdatedAt
        };

        #endregion

        private static ApiPlayerTrackDto RequireTrack(ApiPlayerTrackDto? track)
        {
            if (!PlayerDataRules.TryNormalizeTrack(track, out var normalized, out var error))
                throw new PlayerDataValidationException(error!);
            return normalized;
        }

        private static string RequireKey(string? key) =>
            PlayerDataRules.NormalizeKey(key)
            ?? throw new PlayerDataValidationException("key must be channel:{channelId}:{fileId} or local:{path}");

        private static void RequireLimit(int value, string name)
        {
            if (!PlayerDataRules.IsValidLimit(value))
                throw new PlayerDataValidationException($"{name} must be between {PlayerDataRules.MinLimit} and {PlayerDataRules.MaxLimit}");
        }

        private void Raise(string kind, string? key)
        {
            try
            {
                Changed?.Invoke(new PlayerDataChangedEventDto { Kind = kind, Key = key });
            }
            catch
            {
                // a broken listener must not fail the write
            }
        }
    }
}
