import { useMemo } from 'react';
import { useSearchParams } from 'react-router';
import type { ReviewFilters } from '../queries';

/** The review filters kept in the URL (`?kind=series&channel=123&rule=/Series/`), so a reload or a link keeps them. */
export function useReviewFilterParams() {
  const [params, setParams] = useSearchParams();
  const kindParam = params.get('kind');
  const channelParam = Number(params.get('channel'));
  const channelId = Number.isFinite(channelParam) && channelParam !== 0 ? channelParam : undefined;
  const ruleParam = channelId !== undefined ? (params.get('rule') ?? undefined) : undefined;
  const filters = useMemo<ReviewFilters>(
    () => ({ kind: kindParam === 'series' || kindParam === 'movie' ? kindParam : undefined, channelId, rule: ruleParam }),
    [kindParam, channelId, ruleParam]
  );
  const setFilters = (next: ReviewFilters) =>
    setParams(
      (prev) => {
        const p = new URLSearchParams(prev);
        const set = (key: string, value: string | number | undefined) => (value === undefined ? p.delete(key) : p.set(key, String(value)));
        set('kind', next.kind);
        set('channel', next.channelId);
        set('rule', next.channelId !== undefined ? next.rule : undefined);
        return p;
      },
      { replace: true }
    );
  const active = filters.kind !== undefined || filters.channelId !== undefined;
  return { filters, setFilters, active };
}
