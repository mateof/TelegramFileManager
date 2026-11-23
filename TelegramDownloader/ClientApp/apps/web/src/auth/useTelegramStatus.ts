import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';

/** Shared query of the Telegram session state (`GET /api/v1/auth/status`). */
export function useTelegramStatus() {
  return useQuery({
    queryKey: queryKeys.telegramStatus,
    queryFn: ({ signal }) => api.auth.status(signal),
    staleTime: 60_000
  });
}
