import type { HttpClient } from '../http';
import type { ChannelId } from '../types/channels';
import type { ApiDeleteOperationDto, DeleteOperationStartedDto } from '../types/deleteOperations';
import type { FileIdsRequest, LocalDeleteRequest } from '../types/files';

const BASE = '/api/v1/delete-operations';
const enc = encodeURIComponent;

/**
 * Background deletes with live progress (capability `files.deleteOperations`).
 * Follow an operation with {@link get} (about once a second) or the
 * `DeleteProgress` message of /hubs/events.
 */
export function createDeleteOperationsApi(http: HttpClient) {
  return {
    /** POST /api/v1/channels/{channelId}/files/delete-operations (202): folders recursively, frees the Telegram messages. */
    startChannel: (channelId: ChannelId, body: FileIdsRequest) =>
      http.post<DeleteOperationStartedDto>(`/api/v1/channels/${enc(String(channelId))}/files/delete-operations`, body),
    /** POST /api/v1/local/delete-operations (202) */
    startLocal: (body: LocalDeleteRequest) => http.post<DeleteOperationStartedDto>('/api/v1/local/delete-operations', body),
    /** GET /api/v1/delete-operations: running ones first, then those finished in the last 10 minutes. */
    list: (signal?: AbortSignal) => http.get<ApiDeleteOperationDto[]>(BASE, { signal }),
    /** GET /api/v1/delete-operations/{id} (404 when unknown or expired) */
    get: (id: string, signal?: AbortSignal) => http.get<ApiDeleteOperationDto>(`${BASE}/${enc(id)}`, { signal }),
    /** POST /api/v1/delete-operations/{id}/cancel (202; 409 when it already finished) */
    cancel: (id: string) => http.post<ApiDeleteOperationDto | null>(`${BASE}/${enc(id)}/cancel`)
  };
}

export type DeleteOperationsApi = ReturnType<typeof createDeleteOperationsApi>;
