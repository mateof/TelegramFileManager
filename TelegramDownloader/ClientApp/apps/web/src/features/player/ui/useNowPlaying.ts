import { useEffect } from 'react';
import { getEngine } from '../engine';
import { useTrackMeta } from '../metadata';
import { displayTitle } from '../queue';
import { selectCurrent, usePlayer } from '../store';

/** What the player shows for the current track: tags from the file win over the file name. */
export function useNowPlaying() {
  const track = usePlayer(selectCurrent);
  const meta = useTrackMeta(track);
  const cover = meta?.cover ?? track?.coverUrl ?? null;
  const title = meta?.title || (track ? displayTitle(track) : '');
  const subtitle = meta?.artist || track?.artist || track?.source || '';
  const album = meta?.album || track?.album || '';
  return { track, title, subtitle, album, cover, meta };
}

/** Keeps the lock-screen metadata in sync with the tags (mounted once, by the bar). */
export function useSyncMediaSession() {
  const { track, meta } = useNowPlaying();
  const qid = track?.qid;
  useEffect(() => {
    if (!qid) return;
    getEngine().setNowPlaying({ qid, title: meta?.title, artist: meta?.artist, album: meta?.album, cover: meta?.cover });
  }, [qid, meta]);
}
