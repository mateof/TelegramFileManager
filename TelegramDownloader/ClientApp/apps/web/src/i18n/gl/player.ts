// Strings of the media feature: audio player, queue and viewers (owned by that feature)
const player: Record<string, string> = {
  // engine
  'Could not play "{title}"': 'Non se puido reproducir «{title}»',
  'The track could not be played': 'Non se puido reproducir a pista',
  '{count} tracks will play next': '{count} pistas soarán a continuación',
  '{count} tracks added to the queue': '{count} pistas engadidas á cola',
  'Sleep timer: playback paused': 'Temporizador: reprodución en pausa',

  // controls
  'Play': 'Reproducir',
  'Pause': 'Pausa',
  'Shuffle on': 'Aleatorio activado',
  'Shuffle off': 'Aleatorio desactivado',
  'Repeat off': 'Repetir desactivado',
  'Repeat all': 'Repetir todo',
  'Repeat one': 'Repetir unha',
  'Previous': 'Anterior',
  'Next': 'Seguinte',
  'Mute': 'Silenciar',
  'Unmute': 'Activar o son',
  'Volume': 'Volume',
  'Seek': 'Posición',
  'Sleep timer': 'Temporizador de apagado',
  '{count} minutes': '{count} minutos',
  'End of track': 'Ao rematar a pista',
  'Turn off the timer': 'Desactivar o temporizador',
  'More options': 'Máis opcións',
  'Add to playlist': 'Engadir a unha lista',
  'Save queue as playlist': 'Gardar a cola como lista',
  'Crossfade': 'Fundido entre pistas',
  'Off (gapless)': 'Desactivado (sen pausas)',
  '{count} seconds': '{count} segundos',
  'Clear queue and close': 'Baleirar a cola e pechar',
  '{count} tracks cannot be stored in a playlist and will be left out.':
    '{count} pistas non se poden gardar nunha lista e omitiranse.',
  'My mix': 'A miña mestura',
  'Save': 'Gardar',
  'Name': 'Nome',

  // bar, expanded view, queue
  'Player': 'Reprodutor',
  'Open the player': 'Abrir o reprodutor',
  'Expand': 'Expandir',
  'Now playing': 'Reproducindo',
  'Minimize the player': 'Minimizar o reprodutor',
  'Queue': 'Cola',
  '{count} tracks in the queue': '{count} pistas na cola',
  'Drag or Alt+↑/↓ to reorder': 'Arrastra ou usa Alt+↑/↓ para reordenar',
  'Remove from the queue': 'Quitar da cola',

  // viewers
  'Download': 'Descargar',
  '{index} of {count}': '{index} de {count}',
  'The image could not be loaded': 'Non se puido cargar a imaxe',
  'Zoom in': 'Achegar',
  'Zoom out': 'Afastar',
  'Reset zoom': 'Restablecer o zoom',
  'This file is too large to preview.': 'O ficheiro é demasiado grande para previsualizalo.',
  'The file could not be loaded': 'Non se puido cargar o ficheiro',
  'There is no preview for this kind of file.': 'Non hai vista previa para este tipo de ficheiro.',
  'This video cannot be played in the browser.': 'Este vídeo non se pode reproducir no navegador.',
  'Back 10 seconds': 'Retroceder 10 segundos',
  'Forward 10 seconds': 'Avanzar 10 segundos',
  'Playback speed': 'Velocidade de reprodución',
  'Picture in picture': 'Imaxe en imaxe',
  'Full screen': 'Pantalla completa',
  'Exit full screen': 'Saír da pantalla completa'
};

export default player;
