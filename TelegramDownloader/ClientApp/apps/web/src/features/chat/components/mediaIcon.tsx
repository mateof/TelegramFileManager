import { File as FileIcon, FileText, Film, Image as ImageIcon, Mic, Music } from 'lucide-react';
import type { ReactNode } from 'react';

export function mediaIcon(type: string | null): ReactNode {
  switch (type) {
    case 'audio':
      return <Music />;
    case 'voice':
      return <Mic />;
    case 'video':
      return <Film />;
    case 'photo':
      return <ImageIcon />;
    case 'document':
      return <FileText />;
    default:
      return <FileIcon />;
  }
}
