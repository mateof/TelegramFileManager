# Global search

```
GET /api/v1/search?q=flashes&filter=all&scope=all&limit=200
```

Searches file names (not folders) in **every indexed channel** and in the
server **local storage** at once. It runs on the same in-memory name index as
`GET /api/mobile/search` (built per channel on first use, dropped when the
channel is refreshed and after 10 minutes), so results are the same; the
response uses the v1 envelope and `ApiFileDto`, media URLs included.

| Parameter | Values |
| --- | --- |
| `q` | Required, at least 2 characters. Case-insensitive substring match. |
| `filter` | `all` (default), `audio`, `video`, `photo`, `document`, `archive`. |
| `scope` | `all` (default), `channels`, `local`. |
| `limit` | 1-500 (default 200), applied separately to channel results and to local results. |

```json
{
  "success": true,
  "data": {
    "channels": [
      { "channelId": "1290586824", "channelName": "Music", "files": [ /* ApiFileDto */ ] }
    ],
    "local": [ /* ApiFileDto */ ],
    "truncated": false
  }
}
```

- Channel groups are ordered by channel name; only channels with matches appear.
- `truncated` is true when more matches existed than `limit`.
- A channel whose index cannot be read is skipped; the rest of the search continues.
- No Telegram session is needed (channel names fall back to ids).

To search inside one channel (or one folder of it), use
`GET /api/v1/channels/{channelId}/files/search` ([files.md](files.md#search-a-subtree)).

Next: [local-files.md](local-files.md).
