namespace TelegramDownloader.Models
{
    /// <summary>
    /// One page of a larger result: the items of the page and the total number
    /// of items. UI-neutral; a UI grid builds its own result type from it.
    /// </summary>
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public int TotalCount { get; init; }
    }
}
